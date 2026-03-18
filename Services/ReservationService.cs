using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Models;
using Google.Cloud.Firestore;

namespace Proyecto_Progra_Web.API.Services;

/// <summary>
/// ReservationService implementa la logica de reservas.
///
/// Regla central del PDF: un huesped solo puede reservar UNA vez.
/// Una vez que HasReserved = true, queda bloqueado permanentemente.
/// La reserva es inmutable: no se puede modificar ni cancelar.
/// </summary>
public class ReservationService : IReservationService
{
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<ReservationService> _logger;

    // Impuesto del 15% por defecto, configurable si se necesita
    private const double DefaultTaxRate = 0.15;

    public ReservationService(FirebaseService firebaseService, ILogger<ReservationService> logger)
    {
        _firebaseService = firebaseService;
        _logger = logger;
    }

    
    // CREATE RESERVATION
    

    public async Task<ReservationResponseDto> CreateReservation(
        CreateReservationDto createReservationDto, string userId)
    {
        try
        {
            // Parsear las fechas usando Split desde el formato dd-MM-yyyy
            // El huesped escribe "01-04-2026" y Split lo separa en dia, mes y anio
            var checkIn = ParseFecha(createReservationDto.CheckInDate);
            var checkOut = ParseFecha(createReservationDto.CheckOutDate);

            if (checkIn >= checkOut)
                throw new ArgumentException("La fecha de entrada debe ser anterior a la fecha de salida");

            // Comparar con la fecha actual en hora de Honduras (UTC-6)
            var hondurasNow = DateTime.UtcNow.AddHours(-6).Date;
            if (checkIn < hondurasNow)
                throw new ArgumentException("La fecha de entrada no puede ser en el pasado");

            var usersCollection = _firebaseService.GetCollection("users");
            var roomsCollection = _firebaseService.GetCollection("rooms");
            var reservationsCollection = _firebaseService.GetCollection("reservations");

            // Obtener el usuario que quiere reservar
            var userDoc = await usersCollection.Document(userId).GetSnapshotAsync();
            if (!userDoc.Exists)
                throw new InvalidOperationException("Usuario no encontrado");

            var userDict = userDoc.ToDictionary();

            // VALIDACION CENTRAL: verificar bloqueo permanente
            // Si HasReserved es true, el huesped ya uso su unica reserva
            var hasReserved = userDict.ContainsKey("HasReserved") && (bool)userDict["HasReserved"];
            if (hasReserved)
                throw new InvalidOperationException("Ya realizaste una reserva. No es posible realizar otra.");

            // Buscar la habitacion por su numero visible (RoomNumber)
            // El huesped escribe "504" y el sistema obtiene el ID interno automaticamente
            var roomQuery = await roomsCollection
                .WhereEqualTo("RoomNumber", createReservationDto.RoomNumber)
                .GetSnapshotAsync();

            if (roomQuery.Count == 0)
                throw new InvalidOperationException(
                    $"No existe ninguna habitacion con el numero '{createReservationDto.RoomNumber}'");

            // Tomar el primer resultado (RoomNumber es unico)
            var roomDoc = roomQuery.Documents[0];
            var roomId = roomDoc.Id;
            var roomDict = roomDoc.ToDictionary();

            // Verificar disponibilidad en las fechas solicitadas
            var isAvailable = await IsRoomAvailable(roomId, checkIn, checkOut);

            if (!isAvailable)
                throw new InvalidOperationException(
                    "La habitacion no esta disponible en las fechas seleccionadas");

            // Calcular noches y costos
            var nights = (int)(checkOut - checkIn).TotalDays;
            var baseRate = Convert.ToDouble(roomDict["BaseRate"]);
            var baseAmount = baseRate * nights;
            var taxAmount = baseAmount * DefaultTaxRate;
            var totalCost = CalculateTotalCost(baseRate, nights, DefaultTaxRate);

            // Crear el documento de reserva (registro inmutable de auditoria)
            var newReservation = new Reservation
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                UserName = userDict["Fullname"].ToString()!,
                RoomId = roomId,
                RoomNumber = roomDict["RoomNumber"].ToString()!,
                RoomType = roomDict["RoomType"].ToString()!,
                CheckInDate = checkIn,
                CheckOutDate = checkOut,
                Nights = nights,
                TotalCost = totalCost,
                Status = "confirmed",
                Timestamp = DateTime.UtcNow
            };

            // Preparar datos antes de la transaccion
            var reservedDates = $"{checkIn:dd-MM-yyyy} - {checkOut:dd-MM-yyyy}";
            var currentCount = roomDict.ContainsKey("ReservationCount")
                ? Convert.ToInt32(roomDict["ReservationCount"])
                : 0;
            var reservationTimestamp = DateTime.UtcNow;

            // Ejecutar las 3 operaciones como transaccion atomica de Firestore
            // Si cualquiera falla, ninguna se aplica (todo o nada)
            // Esto garantiza que nunca quede una reserva sin el bloqueo del usuario
            await _firebaseService.RunTransactionAsync(async transaction =>
            {
                // Operacion 1: guardar el documento de reserva en la coleccion "reservations"
                var reservationRef = reservationsCollection.Document(newReservation.Id);
                transaction.Set(reservationRef, newReservation);

                // Operacion 2: marcar HasReserved = true en el usuario (bloqueo permanente)
                // y guardar los datos de su reserva para mostrarlos si intenta volver
                var userRef = usersCollection.Document(userId);
                transaction.Update(userRef, new Dictionary<string, object>
                {
                    { "HasReserved", true },
                    { "ReservedRoomId", roomId },
                    { "ReservedDates", reservedDates },
                    { "ReservationTimestamp", reservationTimestamp }
                });

                // Operacion 3: incrementar el contador de reservas de la habitacion
                var roomRef = roomsCollection.Document(roomId);
                transaction.Update(roomRef, new Dictionary<string, object>
                {
                    { "ReservationCount", currentCount + 1 }
                });
            });

            _logger.LogInformation($"Reserva creada: {newReservation.Id} para usuario {userId}");

            return new ReservationResponseDto
            {
                Success = true,
                Message = "Reserva confirmada exitosamente",
                Reservation = ConvertToDto(newReservation),
                BaseAmount = baseAmount,
                TaxAmount = taxAmount,
                TotalAmount = totalCost
            };
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning($"Validacion en CreateReservation: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al crear reserva: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // GET ALL RESERVATIONS (solo gerente)
    // --------------------------------------------------------

    public async Task<List<ReservationDto>> GetAllReservations()
    {
        try
        {
            var reservationsCollection = _firebaseService.GetCollection("reservations");

            // Ordenar por timestamp descendente para ver las mas recientes primero
            var snapshot = await reservationsCollection
                .OrderByDescending("Timestamp")
                .GetSnapshotAsync();

            var reservations = new List<ReservationDto>();
            foreach (var doc in snapshot.Documents)
            {
                var reservation = MapDocumentToReservation(doc);
                reservations.Add(ConvertToDto(reservation));
            }

            return reservations;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener todas las reservas: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // GET RESERVATION BY USER ID (huesped ve solo la suya)
    // --------------------------------------------------------

    public async Task<ReservationDto?> GetReservationByUserId(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            var reservationsCollection = _firebaseService.GetCollection("reservations");

            var query = await reservationsCollection
                .WhereEqualTo("UserId", userId)
                .GetSnapshotAsync();

            if (query.Count == 0)
                return null;

            // Un huesped solo tiene una reserva, tomamos el primero
            var reservation = MapDocumentToReservation(query.Documents[0]);
            return ConvertToDto(reservation);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener reserva del usuario {userId}: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // IS ROOM AVAILABLE
    // --------------------------------------------------------

    public async Task<bool> IsRoomAvailable(string roomId, DateTime checkIn, DateTime checkOut)
    {
        try
        {
            var reservationsCollection = _firebaseService.GetCollection("reservations");

            // Traer todas las reservas de esa habitacion
            var existingReservations = await reservationsCollection
                .WhereEqualTo("RoomId", roomId)
                .GetSnapshotAsync();

            // Verificar si alguna reserva existente se solapa con las fechas solicitadas
            // Solapamiento: la nueva entrada es antes de que salga la existente
            //               Y la nueva salida es despues de que entre la existente
            foreach (var doc in existingReservations.Documents)
            {
                var dict = doc.ToDictionary();

                var existingCheckIn = ((Timestamp)dict["CheckInDate"]).ToDateTime();
                var existingCheckOut = ((Timestamp)dict["CheckOutDate"]).ToDateTime();

                bool overlaps = checkIn < existingCheckOut && checkOut >= existingCheckIn;

                if (overlaps)
                    return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al verificar disponibilidad: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // CALCULATE TOTAL COST
    // --------------------------------------------------------

    public double CalculateTotalCost(double baseRate, int nights, double taxRate = DefaultTaxRate)
    {
        // Costo base: tarifa por noche multiplicada por la cantidad de noches
        var baseAmount = baseRate * nights;

        // Impuesto aplicado sobre el total base
        var tax = baseAmount * taxRate;

        // Total final redondeado a 2 decimales
        return Math.Round(baseAmount + tax, 2);
    }

    // --------------------------------------------------------
    // METODOS PRIVADOS
    // --------------------------------------------------------

    // Convierte un DocumentSnapshot a objeto Reservation
    private Reservation MapDocumentToReservation(DocumentSnapshot doc)
    {
        var dict = doc.ToDictionary();

        return new Reservation
        {
            Id = doc.Id,
            UserId = dict["UserId"].ToString()!,
            UserName = dict["UserName"].ToString()!,
            RoomId = dict["RoomId"].ToString()!,
            RoomNumber = dict["RoomNumber"].ToString()!,
            RoomType = dict["RoomType"].ToString()!,
            CheckInDate = ((Timestamp)dict["CheckInDate"]).ToDateTime(),
            CheckOutDate = ((Timestamp)dict["CheckOutDate"]).ToDateTime(),
            Nights = Convert.ToInt32(dict["Nights"]),
            TotalCost = Convert.ToDouble(dict["TotalCost"]),
            Status = dict.ContainsKey("Status") ? dict["Status"].ToString()! : "confirmed",
            Timestamp = dict.ContainsKey("Timestamp")
                ? ((Timestamp)dict["Timestamp"]).ToDateTime()
                : DateTime.UtcNow
        };
    }

    // --------------------------------------------------------
    // GET ROOM ID BY NUMBER
    // --------------------------------------------------------

    public async Task<string?> GetRoomIdByNumber(string roomNumber)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roomNumber))
                return null;

            var roomsCollection = _firebaseService.GetCollection("rooms");

            var query = await roomsCollection
                .WhereEqualTo("RoomNumber", roomNumber)
                .GetSnapshotAsync();

            if (query.Count == 0)
                return null;

            return query.Documents[0].Id;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al buscar habitacion por numero: {ex.Message}");
            return null;
        }
    }

    // Parsea un string con formato dd-MM-yyyy a DateTime UTC usando Split
    // Split divide el string por el guion: ["01", "04", "2026"]
    // partes[0] = dia, partes[1] = mes, partes[2] = anio
    private DateTime ParseFecha(string fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha))
            throw new ArgumentException("La fecha no puede estar vacia");

        var partes = fecha.Split('-');

        if (partes.Length != 3)
            throw new ArgumentException($"Formato de fecha invalido: '{fecha}'. Use dd-MM-yyyy. Ejemplo: 01-04-2026");

        if (!int.TryParse(partes[0], out int dia) ||
            !int.TryParse(partes[1], out int mes) ||
            !int.TryParse(partes[2], out int anio))
            throw new ArgumentException($"La fecha '{fecha}' contiene valores no numericos. Use dd-MM-yyyy");

        try
        {
            // DateTimeKind.Utc requerido por Firestore
            return new DateTime(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);
        }
        catch
        {
            throw new ArgumentException($"La fecha '{fecha}' no es valida. Verifique dia, mes y anio");
        }
    }

    // Convierte Reservation (modelo interno) a ReservationDto (lo que se envia al cliente)
    private ReservationDto ConvertToDto(Reservation reservation)
    {
        return new ReservationDto
        {
            Id = reservation.Id,
            UserId = reservation.UserId,
            UserName = reservation.UserName,
            RoomId = reservation.RoomId,
            RoomNumber = reservation.RoomNumber,
            RoomType = reservation.RoomType,
            // Formatear las fechas a dd-MM-yyyy para que sean legibles en la respuesta
            CheckInDate = reservation.CheckInDate.ToString("dd-MM-yyyy"),
            CheckOutDate = reservation.CheckOutDate.ToString("dd-MM-yyyy"),
            Nights = reservation.Nights,
            TotalCost = reservation.TotalCost,
            Status = reservation.Status,
            // Timestamp con fecha y hora completa para auditoria
            Timestamp = reservation.Timestamp.ToString("dd-MM-yyyy HH:mm")
        };
    }
}