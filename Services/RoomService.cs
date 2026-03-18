using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Models;
using Google.Cloud.Firestore;

namespace Proyecto_Progra_Web.API.Services;

/// <summary>
/// RoomService implementa el CRUD de habitaciones contra Firestore.
/// Solo el gerente puede crear, editar y eliminar.
/// Los huespedes solo pueden consultar habitaciones disponibles.
/// </summary>
public class RoomService : IRoomService
{
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<RoomService> _logger;

    public RoomService(FirebaseService firebaseService, ILogger<RoomService> logger)
    {
        _firebaseService = firebaseService;
        _logger = logger;
    }
    
    // GET ALL ROOMS

    public async Task<List<RoomDto>> GetAllRooms(string? roomType = null)
    {
        try
        {
            var roomsCollection = _firebaseService.GetCollection("rooms");
            Query query = roomsCollection;

            // Filtrar por tipo si se indica (Simple, Doble, Suite, etc.)
            if (!string.IsNullOrWhiteSpace(roomType))
                query = query.WhereEqualTo("RoomType", roomType);

            var snapshot = await query.GetSnapshotAsync();

            var rooms = new List<RoomDto>();
            foreach (var doc in snapshot.Documents)
            {
                var room = MapDocumentToRoom(doc);
                rooms.Add(ConvertToDto(room));
            }

            return rooms;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener habitaciones: {ex.Message}");
            throw;
        }
    }
    
    // GET ROOM BY ID

    public async Task<RoomDto?> GetRoomById(string roomId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roomId))
                return null;

            var roomsCollection = _firebaseService.GetCollection("rooms");
            var doc = await roomsCollection.Document(roomId).GetSnapshotAsync();

            if (!doc.Exists)
                return null;

            var room = MapDocumentToRoom(doc);
            return ConvertToDto(room);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener habitacion {roomId}: {ex.Message}");
            throw;
        }
    }
    
    // CREATE ROOM

    public async Task<Room> CreateRoom(CreateRoomDto createRoomDto, string createdByName, string createdById)
    {
        try
        {
            if (createRoomDto == null)
                throw new ArgumentException("Los datos de la habitacion son requeridos");

            if (string.IsNullOrWhiteSpace(createRoomDto.RoomNumber))
                throw new ArgumentException("El numero de habitacion es requerido");

            if (string.IsNullOrWhiteSpace(createRoomDto.RoomType))
                throw new ArgumentException("El tipo de habitacion es requerido");

            if (createRoomDto.Capacity <= 0)
                throw new ArgumentException("La capacidad debe ser mayor a cero");

            if (createRoomDto.BaseRate <= 0)
                throw new ArgumentException("La tarifa base debe ser mayor a cero");

            var roomsCollection = _firebaseService.GetCollection("rooms");

            // Verificar que el numero de habitacion no este duplicado
            var existingQuery = await roomsCollection
                .WhereEqualTo("RoomNumber", createRoomDto.RoomNumber)
                .GetSnapshotAsync();

            if (existingQuery.Count > 0)
                throw new InvalidOperationException($"Ya existe una habitacion con el numero {createRoomDto.RoomNumber}");

            var newRoom = new Room
            {
                Id = Guid.NewGuid().ToString(),
                RoomNumber = createRoomDto.RoomNumber,
                RoomType = createRoomDto.RoomType,
                Capacity = createRoomDto.Capacity,
                Amenities = createRoomDto.Amenities ?? new List<string>(),
                BaseRate = createRoomDto.BaseRate,
                Description = createRoomDto.Description ?? string.Empty,
                PhotoUrl = createRoomDto.PhotoUrl ?? string.Empty,
                ReservationCount = 0,
                CreatedAt = DateTime.UtcNow,
                CreatedBy = createdByName,
                CreatedById = createdById
            };

            // SetAsync con el objeto directamente usando los atributos (FSData)
            await roomsCollection.Document(newRoom.Id).SetAsync(newRoom);

            _logger.LogInformation($"Habitacion creada: {newRoom.RoomNumber} por {createdByName}");
            return newRoom;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning($"Validacion en CreateRoom: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al crear habitacion: {ex.Message}");
            throw;
        }
    }
    
    // UPDATE ROOM

    public async Task<Room> UpdateRoom(string roomId, UpdateRoomDto updateRoomDto, string updatedByName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("El ID de la habitacion es requerido");

            var roomsCollection = _firebaseService.GetCollection("rooms");
            var doc = await roomsCollection.Document(roomId).GetSnapshotAsync();

            if (!doc.Exists)
                throw new InvalidOperationException($"Habitacion con ID {roomId} no existe");

            var existingRoom = MapDocumentToRoom(doc);

            // Actualizar solo los campos que llegan en el DTO (los null se ignoran)
            if (!string.IsNullOrWhiteSpace(updateRoomDto.RoomType))
                existingRoom.RoomType = updateRoomDto.RoomType;

            if (updateRoomDto.Capacity.HasValue && updateRoomDto.Capacity.Value > 0)
                existingRoom.Capacity = updateRoomDto.Capacity.Value;

            if (updateRoomDto.Amenities != null)
                existingRoom.Amenities = updateRoomDto.Amenities;

            if (updateRoomDto.BaseRate.HasValue && updateRoomDto.BaseRate.Value > 0)
                existingRoom.BaseRate = updateRoomDto.BaseRate.Value;

            if (!string.IsNullOrWhiteSpace(updateRoomDto.Description))
                existingRoom.Description = updateRoomDto.Description;

            if (!string.IsNullOrWhiteSpace(updateRoomDto.PhotoUrl))
                existingRoom.PhotoUrl = updateRoomDto.PhotoUrl;

            // MergeAll: solo sobreescribe los campos enviados, no borra los demas
            await roomsCollection.Document(roomId).SetAsync(existingRoom, SetOptions.MergeAll);

            _logger.LogInformation($"Habitacion {roomId} actualizada por {updatedByName}");
            return existingRoom;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al actualizar habitacion {roomId}: {ex.Message}");
            throw;
        }
    }
    
    // DELETE ROOM

    public async Task DeleteRoom(string roomId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roomId))
                throw new ArgumentException("El ID de la habitacion es requerido");

            var roomsCollection = _firebaseService.GetCollection("rooms");
            var reservationsCollection = _firebaseService.GetCollection("reservations");

            var roomDoc = await roomsCollection.Document(roomId).GetSnapshotAsync();

            if (!roomDoc.Exists)
                throw new InvalidOperationException($"Habitacion con ID {roomId} no existe");

            // Verificar que no tenga reservas antes de eliminar (segun el PDF)
            var reservationsQuery = await reservationsCollection
                .WhereEqualTo("RoomId", roomId)
                .GetSnapshotAsync();

            if (reservationsQuery.Count > 0)
                throw new InvalidOperationException(
                    $"No se puede eliminar la habitacion. Tiene {reservationsQuery.Count} reserva(s) registrada(s).");

            await roomsCollection.Document(roomId).DeleteAsync();

            _logger.LogInformation($"Habitacion {roomId} eliminada");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al eliminar habitacion {roomId}: {ex.Message}");
            throw;
        }
    }
    
    // SEARCH ROOMS

    public async Task<List<RoomDto>> SearchRooms(string searchTerm)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(searchTerm))
                return new List<RoomDto>();

            // Traer todas y filtrar en memoria (Firestore no soporta LIKE nativo)
            var allRooms = await GetAllRooms();
            var term = searchTerm.ToLower();

            return allRooms
                .Where(r => r.RoomNumber.ToLower().Contains(term) ||
                            r.RoomType.ToLower().Contains(term) ||
                            r.Description.ToLower().Contains(term))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al buscar habitaciones: {ex.Message}");
            throw;
        }
    }
    
    // GET AVAILABLE ROOMS

    public async Task<List<RoomDto>> GetAvailableRooms(DateTime checkIn, DateTime checkOut)
    {
        try
        {
            var roomsCollection = _firebaseService.GetCollection("rooms");
            var reservationsCollection = _firebaseService.GetCollection("reservations");

            // Obtener todas las habitaciones registradas
            var allRoomsSnapshot = await roomsCollection.GetSnapshotAsync();

            // Obtener todas las reservas para verificar solapamiento
            var allReservationsSnapshot = await reservationsCollection.GetSnapshotAsync();

            // Construir un set de roomIds que NO estan disponibles en el rango dado
            // Una habitacion no esta disponible si tiene al menos una reserva que se solapa
            var unavailableRoomIds = new HashSet<string>();

            foreach (var doc in allReservationsSnapshot.Documents)
            {
                var dict = doc.ToDictionary();

                if (!dict.ContainsKey("CheckInDate") || !dict.ContainsKey("CheckOutDate"))
                    continue;

                var existingCheckIn = ((Google.Cloud.Firestore.Timestamp)dict["CheckInDate"]).ToDateTime();
                var existingCheckOut = ((Google.Cloud.Firestore.Timestamp)dict["CheckOutDate"]).ToDateTime();

                // Solapamiento: la nueva entrada es antes de que salga la existente
                //               Y la nueva salida es despues de que entre la existente
                bool overlaps = checkIn < existingCheckOut && checkOut >= existingCheckIn;

                if (overlaps && dict.ContainsKey("RoomId"))
                    unavailableRoomIds.Add(dict["RoomId"].ToString()!);
            }

            // Filtrar solo las habitaciones que NO estan en el set de no disponibles
            var availableRooms = new List<RoomDto>();

            foreach (var doc in allRoomsSnapshot.Documents)
            {
                // Si el ID de la habitacion no esta en el set de no disponibles, esta libre
                if (!unavailableRoomIds.Contains(doc.Id))
                {
                    var room = MapDocumentToRoom(doc);
                    availableRooms.Add(ConvertToDto(room));
                }
            }

            return availableRooms;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener habitaciones disponibles: {ex.Message}");
            throw;
        }
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

            // RoomNumber es unico, tomar el primer resultado
            return query.Documents[0].Id;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al buscar habitacion por numero: {ex.Message}");
            return null;
        }
    }

    // --------------------------------------------------------
    // METODOS PRIVADOS
    // --------------------------------------------------------

    // Convierte un DocumentSnapshot de Firestore a objeto Room
    private Room MapDocumentToRoom(DocumentSnapshot doc)
    {
        var dict = doc.ToDictionary();

        var room = new Room
        {
            Id = doc.Id,
            RoomNumber = dict.ContainsKey("RoomNumber") ? dict["RoomNumber"].ToString()! : string.Empty,
            RoomType = dict.ContainsKey("RoomType") ? dict["RoomType"].ToString()! : string.Empty,
            Capacity = dict.ContainsKey("Capacity") ? Convert.ToInt32(dict["Capacity"]) : 0,
            BaseRate = dict.ContainsKey("BaseRate") ? Convert.ToDouble(dict["BaseRate"]) : 0,
            Description = dict.ContainsKey("Description") ? dict["Description"].ToString()! : string.Empty,
            PhotoUrl = dict.ContainsKey("PhotoUrl") ? dict["PhotoUrl"].ToString()! : string.Empty,
            ReservationCount = dict.ContainsKey("ReservationCount") ? Convert.ToInt32(dict["ReservationCount"]) : 0,
            CreatedBy = dict.ContainsKey("CreatedBy") ? dict["CreatedBy"].ToString()! : string.Empty,
            CreatedById = dict.ContainsKey("CreatedById") ? dict["CreatedById"].ToString()! : string.Empty,
            CreatedAt = dict.ContainsKey("CreatedAt")
                ? ((Timestamp)dict["CreatedAt"]).ToDateTime()
                : DateTime.UtcNow
        };

        // Amenities se guarda como lista en Firestore
        if (dict.ContainsKey("Amenities") && dict["Amenities"] is List<object> rawList)
            room.Amenities = rawList.Select(a => a.ToString()!).ToList();

        return room;
    }

    // Convierte Room (modelo interno) a RoomDto (lo que se envia al cliente)
    private RoomDto ConvertToDto(Room room)
    {
        return new RoomDto
        {
            Id = room.Id,
            RoomNumber = room.RoomNumber,
            RoomType = room.RoomType,
            Capacity = room.Capacity,
            Amenities = room.Amenities,
            BaseRate = room.BaseRate,
            Description = room.Description,
            PhotoUrl = room.PhotoUrl,
            ReservationCount = room.ReservationCount,
            CreatedBy = room.CreatedBy,
            CreatedAt = room.CreatedAt
        };
    }
}