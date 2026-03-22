using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Models;
using Google.Cloud.Firestore;

namespace Proyecto_Progra_Web.API.Services;

/// <summary>
/// ReservationService implementa la logica de reservas.
/// </summary>
public class ReservationService : IReservationService
{
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<ReservationService> _logger;

    private const double DefaultTaxRate = 0.15;

    public ReservationService(FirebaseService firebaseService, ILogger<ReservationService> logger)
    {
        _firebaseService = firebaseService;
        _logger = logger;
    }

    // --------------------------------------------------------
    // CREATE RESERVATION
    // --------------------------------------------------------

    public async Task<ReservationResponseDto> CreateReservation(
        CreateReservationDto createReservationDto, string userId)
    {
        try
        {
            var checkIn  = ParseFecha(createReservationDto.CheckInDate);
            var checkOut = ParseFecha(createReservationDto.CheckOutDate);

            if (checkIn >= checkOut)
                throw new ArgumentException("La fecha de entrada debe ser anterior a la fecha de salida");

            var hondurasNow = DateTime.UtcNow.AddHours(-6).Date;
            if (checkIn < hondurasNow)
                throw new ArgumentException("La fecha de entrada no puede ser en el pasado");

            var usersCollection        = _firebaseService.GetCollection("users");
            var roomsCollection        = _firebaseService.GetCollection("rooms");
            var reservationsCollection = _firebaseService.GetCollection("reservations");

            var userDoc = await usersCollection.Document(userId).GetSnapshotAsync();
            if (!userDoc.Exists)
                throw new InvalidOperationException("Usuario no encontrado");

            var userDict = userDoc.ToDictionary();

            var isActive = !userDict.ContainsKey("IsActive") || (bool)userDict["IsActive"];
            if (!isActive)
                throw new InvalidOperationException("Tu cuenta ha sido desactivada. Contacta al administrador.");

            var hasReserved = userDict.ContainsKey("HasReserved") && (bool)userDict["HasReserved"];
            if (hasReserved)
            {
                // Verificar si la reserva existente ya expiró (checkOut < ahora Honduras UTC-6)
                var existingReservationQuery = await reservationsCollection
                    .WhereEqualTo("UserId", userId)
                    .GetSnapshotAsync();

                bool existingIsExpiredOrCancelled = true;
                foreach (var resDoc in existingReservationQuery.Documents)
                {
                    var resDict = resDoc.ToDictionary();
                    var resStatus = resDict.ContainsKey("Status") ? resDict["Status"].ToString() : "confirmed";
                    if (resStatus == "cancelled") continue;

                    var resCheckOut = ((Google.Cloud.Firestore.Timestamp)resDict["CheckOutDate"]).ToDateTime();
                    var hondurasNowCheck = DateTime.UtcNow.AddHours(-6);
                    if (resCheckOut >= hondurasNowCheck)
                    {
                        // Hay una reserva vigente — no permitir nueva reserva
                        existingIsExpiredOrCancelled = false;
                        break;
                    }
                }

                if (!existingIsExpiredOrCancelled)
                    throw new InvalidOperationException("Ya tienes una reserva activa. No es posible realizar otra.");

                // La reserva existente ya expiró — limpiar el flag HasReserved en Firestore
                // para que futuras llamadas no pasen por esta verificación costosa
                await usersCollection.Document(userId).UpdateAsync(new Dictionary<string, object>
                {
                    { "HasReserved",    false },
                    { "ReservedRoomId", "" },
                    { "ReservedDates",  "" }
                });
            }

            var roomQuery = await roomsCollection
                .WhereEqualTo("RoomNumber", createReservationDto.RoomNumber)
                .GetSnapshotAsync();

            if (roomQuery.Count == 0)
                throw new InvalidOperationException(
                    $"No existe ninguna habitacion con el numero '{createReservationDto.RoomNumber}'");

            var roomDoc  = roomQuery.Documents[0];
            var roomId   = roomDoc.Id;
            var roomDict = roomDoc.ToDictionary();

            var isAvailable = await IsRoomAvailable(roomId, checkIn, checkOut);
            if (!isAvailable)
                throw new InvalidOperationException(
                    "La habitacion no esta disponible en las fechas seleccionadas");

            var nights     = (int)(checkOut - checkIn).TotalDays;
            var baseRate   = Convert.ToDouble(roomDict["BaseRate"]);
            var baseAmount = baseRate * nights;
            var taxAmount  = baseAmount * DefaultTaxRate;
            var totalCost  = CalculateTotalCost(baseRate, nights, DefaultTaxRate);

            var newReservation = new Reservation
            {
                Id           = Guid.NewGuid().ToString(),
                UserId       = userId,
                UserName     = userDict["Fullname"].ToString()!,
                RoomId       = roomId,
                RoomNumber   = roomDict["RoomNumber"].ToString()!,
                RoomType     = roomDict["RoomType"].ToString()!,
                CheckInDate  = checkIn,
                CheckOutDate = checkOut,
                Nights       = nights,
                TotalCost    = totalCost,
                Status       = "confirmed",
                Timestamp    = DateTime.UtcNow
            };

            var reservedDates        = $"{checkIn:dd-MM-yyyy} - {checkOut:dd-MM-yyyy}";
            var currentCount         = roomDict.ContainsKey("ReservationCount") ? Convert.ToInt32(roomDict["ReservationCount"]) : 0;
            var reservationTimestamp = DateTime.UtcNow;

            await _firebaseService.RunTransactionAsync(async transaction =>
            {
                var reservationRef = reservationsCollection.Document(newReservation.Id);
                transaction.Set(reservationRef, newReservation);

                var userRef = usersCollection.Document(userId);
                transaction.Update(userRef, new Dictionary<string, object>
                {
                    { "HasReserved",         true },
                    { "ReservedRoomId",      roomId },
                    { "ReservedDates",       reservedDates },
                    { "ReservationTimestamp", reservationTimestamp }
                });

                var roomRef = roomsCollection.Document(roomId);
                transaction.Update(roomRef, new Dictionary<string, object>
                {
                    { "ReservationCount", currentCount + 1 }
                });
            });

            _logger.LogInformation($"Reserva creada: {newReservation.Id} para usuario {userId}");

            return new ReservationResponseDto
            {
                Success     = true,
                Message     = "Reserva confirmada exitosamente",
                Reservation = ConvertToDto(newReservation),
                BaseAmount  = baseAmount,
                TaxAmount   = taxAmount,
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
    // GET RESERVATION BY USER ID
    // --------------------------------------------------------

    public async Task<ReservationDto?> GetReservationByUserId(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId)) return null;

            var reservationsCollection = _firebaseService.GetCollection("reservations");

            var query = await reservationsCollection
                .WhereEqualTo("UserId", userId)
                .GetSnapshotAsync();

            if (query.Count == 0) return null;

            // Ordenar en memoria por Timestamp desc para evitar requerir índice compuesto en Firestore
            // Devuelve la reserva más reciente del usuario (puede tener varias si canceló y volvió a reservar)
            var mostRecent = query.Documents
                .Select(doc => MapDocumentToReservation(doc))
                .OrderByDescending(r => r.Timestamp)
                .First();

            return ConvertToDto(mostRecent);
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

            var existingReservations = await reservationsCollection
                .WhereEqualTo("RoomId", roomId)
                .GetSnapshotAsync();

            foreach (var doc in existingReservations.Documents)
            {
                var dict   = doc.ToDictionary();
                var status = dict.ContainsKey("Status") ? dict["Status"].ToString() : "confirmed";
                if (status == "cancelled") continue;

                var existingCheckIn  = ((Timestamp)dict["CheckInDate"]).ToDateTime();
                var existingCheckOut = ((Timestamp)dict["CheckOutDate"]).ToDateTime();

                bool overlaps = checkIn < existingCheckOut && checkOut >= existingCheckIn;
                if (overlaps) return false;
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
        var baseAmount = baseRate * nights;
        var tax        = baseAmount * taxRate;
        return Math.Round(baseAmount + tax, 2);
    }

    // --------------------------------------------------------
    // CANCEL RESERVATION (gerente — sin restriccion de tiempo)
    // --------------------------------------------------------

    public async Task CancelReservation(string reservationId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(reservationId))
                throw new ArgumentException("El ID de la reserva es requerido");

            var reservationsCollection = _firebaseService.GetCollection("reservations");
            var usersCollection        = _firebaseService.GetCollection("users");
            var roomsCollection        = _firebaseService.GetCollection("rooms");

            var reservationDoc = await reservationsCollection.Document(reservationId).GetSnapshotAsync();
            if (!reservationDoc.Exists)
                throw new InvalidOperationException("Reserva no encontrada");

            var reservationDict = reservationDoc.ToDictionary();
            var currentStatus   = reservationDict.ContainsKey("Status") ? reservationDict["Status"].ToString() : "confirmed";

            if (currentStatus == "cancelled")
                throw new InvalidOperationException("La reserva ya fue cancelada anteriormente");

            var userId = reservationDict["UserId"].ToString()!;
            var roomId = reservationDict["RoomId"].ToString()!;

            var roomDoc      = await roomsCollection.Document(roomId).GetSnapshotAsync();
            var currentCount = 0;
            if (roomDoc.Exists)
            {
                var roomDict = roomDoc.ToDictionary();
                currentCount = roomDict.ContainsKey("ReservationCount") ? Convert.ToInt32(roomDict["ReservationCount"]) : 0;
            }

            var roomNumber = reservationDict.ContainsKey("RoomNumber") ? reservationDict["RoomNumber"].ToString() : "?";
            var checkIn    = reservationDict.ContainsKey("CheckInDate")
                ? ((Google.Cloud.Firestore.Timestamp)reservationDict["CheckInDate"]).ToDateTime().ToString("dd-MM-yyyy")
                : "?";
            var checkOut   = reservationDict.ContainsKey("CheckOutDate")
                ? ((Google.Cloud.Firestore.Timestamp)reservationDict["CheckOutDate"]).ToDateTime().ToString("dd-MM-yyyy")
                : "?";

            var notificationsCollection = _firebaseService.GetCollection("notifications");
            var notifId = Guid.NewGuid().ToString();

            await _firebaseService.RunTransactionAsync(async transaction =>
            {
                var reservationRef = reservationsCollection.Document(reservationId);
                transaction.Update(reservationRef, new Dictionary<string, object>
                {
                    { "Status", "cancelled" }
                });

                var userRef = usersCollection.Document(userId);
                transaction.Update(userRef, new Dictionary<string, object>
                {
                    { "HasReserved",    false },
                    { "ReservedRoomId", "" },
                    { "ReservedDates",  "" }
                });

                var roomRef = roomsCollection.Document(roomId);
                transaction.Update(roomRef, new Dictionary<string, object>
                {
                    { "ReservationCount", Math.Max(0, currentCount - 1) }
                });

                // Notificacion para el huesped: su reserva fue cancelada por el gerente
                var notifRef = notificationsCollection.Document(notifId);
                transaction.Set(notifRef, new Dictionary<string, object>
                {
                    { "UserId",    userId },
                    { "Type",      "reservation_cancelled" },
                    { "Title",     "Reserva cancelada" },
                    { "Message",   $"Tu reserva en Hab. {roomNumber} ({checkIn} → {checkOut}) fue cancelada por el hotel. Puedes realizar una nueva reserva." },
                    { "Icon",      "❌" },
                    { "Read",      false },
                    { "CreatedAt", DateTime.UtcNow }
                });
            });

            _logger.LogInformation($"Reserva {reservationId} cancelada por el gerente. Huesped {userId} liberado.");
        }
        catch (ArgumentException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError($"Error al cancelar reserva {reservationId}: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // CANCEL RESERVATION BY GUEST
    // Solo puede cancelar su propia reserva y con 24h de anticipacion
    // --------------------------------------------------------

    public async Task CancelReservationByGuest(string reservationId, string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(reservationId))
                throw new ArgumentException("El ID de la reserva es requerido");

            var reservationsCollection = _firebaseService.GetCollection("reservations");
            var usersCollection        = _firebaseService.GetCollection("users");
            var roomsCollection        = _firebaseService.GetCollection("rooms");

            var reservationDoc = await reservationsCollection.Document(reservationId).GetSnapshotAsync();
            if (!reservationDoc.Exists)
                throw new InvalidOperationException("Reserva no encontrada");

            var dict = reservationDoc.ToDictionary();

            // Verificar que la reserva pertenece al huesped
            var reservationUserId = dict["UserId"].ToString()!;
            if (reservationUserId != userId)
                throw new InvalidOperationException("No tienes permiso para cancelar esta reserva");

            var currentStatus = dict.ContainsKey("Status") ? dict["Status"].ToString() : "confirmed";
            if (currentStatus == "cancelled")
                throw new InvalidOperationException("Esta reserva ya fue cancelada");

            // Verificar la regla de 24 horas
            var checkInDate    = ((Timestamp)dict["CheckInDate"]).ToDateTime();
            var hoursToCheckIn = (checkInDate - DateTime.UtcNow).TotalHours;

            if (hoursToCheckIn <= 24)
                throw new InvalidOperationException(
                    "No puedes cancelar con menos de 24 horas antes del check-in. Contacta con recepcion.");

            var roomId = dict["RoomId"].ToString()!;

            var roomDoc      = await roomsCollection.Document(roomId).GetSnapshotAsync();
            var currentCount = 0;
            if (roomDoc.Exists)
            {
                var roomDict = roomDoc.ToDictionary();
                currentCount = roomDict.ContainsKey("ReservationCount") ? Convert.ToInt32(roomDict["ReservationCount"]) : 0;
            }

            await _firebaseService.RunTransactionAsync(async transaction =>
            {
                var reservationRef = reservationsCollection.Document(reservationId);
                transaction.Update(reservationRef, new Dictionary<string, object>
                {
                    { "Status", "cancelled" }
                });

                var userRef = usersCollection.Document(userId);
                transaction.Update(userRef, new Dictionary<string, object>
                {
                    { "HasReserved",    false },
                    { "ReservedRoomId", "" },
                    { "ReservedDates",  "" }
                });

                var roomRef = roomsCollection.Document(roomId);
                transaction.Update(roomRef, new Dictionary<string, object>
                {
                    { "ReservationCount", Math.Max(0, currentCount - 1) }
                });
            });

            _logger.LogInformation($"Huesped {userId} cancelo su reserva {reservationId}");
        }
        catch (ArgumentException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError($"Error en CancelReservationByGuest: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // MODIFY RESERVATION
    // El huesped cambia las fechas validando disponibilidad
    // y que sea con mas de 24h de anticipacion al check-in actual
    // --------------------------------------------------------

    public async Task ModifyReservation(string reservationId, string userId, string newCheckIn, string newCheckOut)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(reservationId))
                throw new ArgumentException("El ID de la reserva es requerido");

            var checkIn  = ParseFecha(newCheckIn);
            var checkOut = ParseFecha(newCheckOut);

            if (checkIn >= checkOut)
                throw new ArgumentException("La fecha de entrada debe ser anterior a la de salida");

            var hondurasNow = DateTime.UtcNow.AddHours(-6).Date;
            if (checkIn < hondurasNow)
                throw new ArgumentException("La fecha de entrada no puede ser en el pasado");

            var reservationsCollection = _firebaseService.GetCollection("reservations");

            var reservationDoc = await reservationsCollection.Document(reservationId).GetSnapshotAsync();
            if (!reservationDoc.Exists)
                throw new InvalidOperationException("Reserva no encontrada");

            var dict = reservationDoc.ToDictionary();

            // Verificar que pertenece al huesped
            if (dict["UserId"].ToString() != userId)
                throw new InvalidOperationException("No tienes permiso para modificar esta reserva");

            var currentStatus = dict.ContainsKey("Status") ? dict["Status"].ToString() : "confirmed";
            if (currentStatus == "cancelled")
                throw new InvalidOperationException("No puedes modificar una reserva cancelada");

            // Verificar regla de 24 horas sobre el check-in ACTUAL
            var currentCheckIn = ((Timestamp)dict["CheckInDate"]).ToDateTime();
            if ((currentCheckIn - DateTime.UtcNow).TotalHours <= 24)
                throw new InvalidOperationException(
                    "No puedes modificar con menos de 24 horas antes del check-in. Contacta con recepcion.");

            var roomId = dict["RoomId"].ToString()!;

            // Verificar disponibilidad en las nuevas fechas, excluyendo la propia reserva
            var existingReservations = await reservationsCollection
                .WhereEqualTo("RoomId", roomId)
                .GetSnapshotAsync();

            foreach (var doc in existingReservations.Documents)
            {
                if (doc.Id == reservationId) continue; // Ignorar la propia reserva

                var d      = doc.ToDictionary();
                var status = d.ContainsKey("Status") ? d["Status"].ToString() : "confirmed";
                if (status == "cancelled") continue;

                var existingIn  = ((Timestamp)d["CheckInDate"]).ToDateTime();
                var existingOut = ((Timestamp)d["CheckOutDate"]).ToDateTime();

                if (checkIn < existingOut && checkOut >= existingIn)
                    throw new InvalidOperationException(
                        "La habitacion no esta disponible en esas fechas. Elige otras.");
            }

            // Recalcular noches y costo
            var roomsCollection = _firebaseService.GetCollection("rooms");
            var roomDoc         = await roomsCollection.Document(roomId).GetSnapshotAsync();
            var baseRate        = 0.0;
            if (roomDoc.Exists)
                baseRate = Convert.ToDouble(roomDoc.ToDictionary()["BaseRate"]);

            var nights    = (int)(checkOut - checkIn).TotalDays;
            var totalCost = CalculateTotalCost(baseRate, nights, DefaultTaxRate);
            var newDates  = $"{checkIn:dd-MM-yyyy} - {checkOut:dd-MM-yyyy}";

            // Actualizar la reserva y el usuario en una transaccion atomica
            await _firebaseService.RunTransactionAsync(async transaction =>
            {
                var reservationRef = reservationsCollection.Document(reservationId);
                transaction.Update(reservationRef, new Dictionary<string, object>
                {
                    { "CheckInDate",  checkIn },
                    { "CheckOutDate", checkOut },
                    { "Nights",       nights },
                    { "TotalCost",    totalCost }
                });

                var usersCollection = _firebaseService.GetCollection("users");
                var userRef         = usersCollection.Document(userId);
                transaction.Update(userRef, new Dictionary<string, object>
                {
                    { "ReservedDates", newDates }
                });
            });

            _logger.LogInformation($"Reserva {reservationId} modificada por huesped {userId}");
        }
        catch (ArgumentException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError($"Error en ModifyReservation: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // BLOCK GUEST
    // Desactiva permanentemente la cuenta del huesped
    // --------------------------------------------------------

    public async Task<List<ReservationDto>> GetReservationHistoryByUserId(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId)) return new List<ReservationDto>();

            var reservationsCollection = _firebaseService.GetCollection("reservations");

            var query = await reservationsCollection
                .WhereEqualTo("UserId", userId)
                .GetSnapshotAsync();

            if (query.Count == 0) return new List<ReservationDto>();

            return query.Documents
                .Select(doc => ConvertToDto(MapDocumentToReservation(doc)))
                .OrderByDescending(r => r.Timestamp)
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener historial del usuario {userId}: {ex.Message}");
            throw;
        }
    }

    public async Task BlockGuest(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
                throw new ArgumentException("El ID del huesped es requerido");

            var usersCollection = _firebaseService.GetCollection("users");

            var userDoc = await usersCollection.Document(userId).GetSnapshotAsync();
            if (!userDoc.Exists)
                throw new InvalidOperationException("Huesped no encontrado");

            await usersCollection.Document(userId).UpdateAsync(
                new Dictionary<string, object>
                {
                    { "IsActive", false }
                }
            );

            _logger.LogInformation($"Huesped {userId} bloqueado por el gerente.");
        }
        catch (ArgumentException) { throw; }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            _logger.LogError($"Error al bloquear huesped {userId}: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // METODOS PRIVADOS
    // --------------------------------------------------------

    private Reservation MapDocumentToReservation(DocumentSnapshot doc)
    {
        var dict = doc.ToDictionary();

        return new Reservation
        {
            Id           = doc.Id,
            UserId       = dict["UserId"].ToString()!,
            UserName     = dict["UserName"].ToString()!,
            RoomId       = dict["RoomId"].ToString()!,
            RoomNumber   = dict["RoomNumber"].ToString()!,
            RoomType     = dict["RoomType"].ToString()!,
            CheckInDate  = ((Timestamp)dict["CheckInDate"]).ToDateTime(),
            CheckOutDate = ((Timestamp)dict["CheckOutDate"]).ToDateTime(),
            Nights       = Convert.ToInt32(dict["Nights"]),
            TotalCost    = Convert.ToDouble(dict["TotalCost"]),
            Status       = dict.ContainsKey("Status") ? dict["Status"].ToString()! : "confirmed",
            Timestamp    = dict.ContainsKey("Timestamp")
                ? ((Timestamp)dict["Timestamp"]).ToDateTime()
                : DateTime.UtcNow
        };
    }

    public async Task<string?> GetRoomIdByNumber(string roomNumber)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roomNumber)) return null;

            var roomsCollection = _firebaseService.GetCollection("rooms");

            var query = await roomsCollection
                .WhereEqualTo("RoomNumber", roomNumber)
                .GetSnapshotAsync();

            return query.Count == 0 ? null : query.Documents[0].Id;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al buscar habitacion por numero: {ex.Message}");
            return null;
        }
    }

    private DateTime ParseFecha(string fecha)
    {
        if (string.IsNullOrWhiteSpace(fecha))
            throw new ArgumentException("La fecha no puede estar vacia");

        var partes = fecha.Split('-');

        if (partes.Length != 3)
            throw new ArgumentException($"Formato de fecha invalido: '{fecha}'. Use dd-MM-yyyy.");

        if (!int.TryParse(partes[0], out int dia) ||
            !int.TryParse(partes[1], out int mes) ||
            !int.TryParse(partes[2], out int anio))
            throw new ArgumentException($"La fecha '{fecha}' contiene valores no numericos.");

        try
        {
            return new DateTime(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);
        }
        catch
        {
            throw new ArgumentException($"La fecha '{fecha}' no es valida.");
        }
    }

    private ReservationDto ConvertToDto(Reservation reservation)
    {
        return new ReservationDto
        {
            Id           = reservation.Id,
            UserId       = reservation.UserId,
            UserName     = reservation.UserName,
            RoomId       = reservation.RoomId,
            RoomNumber   = reservation.RoomNumber,
            RoomType     = reservation.RoomType,
            CheckInDate  = reservation.CheckInDate.ToString("dd-MM-yyyy"),
            CheckOutDate = reservation.CheckOutDate.ToString("dd-MM-yyyy"),
            Nights       = reservation.Nights,
            TotalCost    = reservation.TotalCost,
            Status       = reservation.Status,
            Timestamp    = reservation.Timestamp.ToString("dd-MM-yyyy HH:mm")
        };
    }
}