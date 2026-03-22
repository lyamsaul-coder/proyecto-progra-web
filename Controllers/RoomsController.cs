using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Services;
using Proyecto_Progra_Web.API.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;

namespace Proyecto_Progra_Web.API.Controllers;

/// <summary>
/// RoomsController maneja la gestion de habitaciones y resenas.
///
/// Endpoints publicos (sin token):
///   GET /api/rooms              - listar habitaciones
///   GET /api/rooms/{number}     - obtener una habitacion
///   GET /api/rooms/available    - habitaciones disponibles en fechas
///   GET /api/rooms/search       - buscar habitaciones
///   GET /api/rooms/{number}/reviews - ver resenas de una habitacion
///
/// Endpoints del huesped (requieren token):
///   POST /api/rooms/{number}/reviews - dejar resena (solo si estadía termino)
///
/// Endpoints del gerente (requieren token con rol "manager"):
///   POST   /api/rooms       - crear habitacion
///   PUT    /api/rooms/{id}  - editar habitacion
///   DELETE /api/rooms/{id}  - eliminar habitacion
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class RoomsController : ControllerBase
{
    private readonly IRoomService _roomService;
    private readonly ILogger<RoomsController> _logger;
    private readonly FirebaseService _firebaseService;

    public RoomsController(IRoomService roomService, ILogger<RoomsController> logger, FirebaseService firebaseService)
    {
        _roomService     = roomService;
        _logger          = logger;
        _firebaseService = firebaseService;
    }

    // --------------------------------------------------------
    // GET /api/rooms
    // Disponible para todos
    // --------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> GetAllRooms([FromQuery] string? roomType = null)
    {
        try
        {
            var rooms = await _roomService.GetAllRooms(roomType);
            return Ok(rooms);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener habitaciones: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener habitaciones" });
        }
    }

    // --------------------------------------------------------
    // GET /api/rooms/{roomNumber}
    // Disponible para todos
    // --------------------------------------------------------
    [HttpGet("{roomNumber}")]
    public async Task<IActionResult> GetRoomByNumber(string roomNumber)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roomNumber))
                return BadRequest(new { message = "El numero de habitacion es requerido" });

            var roomId = await _roomService.GetRoomIdByNumber(roomNumber);
            if (roomId == null)
                return NotFound(new { message = $"No existe ninguna habitacion con el numero '{roomNumber}'" });

            var room = await _roomService.GetRoomById(roomId);
            if (room == null)
                return NotFound(new { message = "Habitacion no encontrada" });

            return Ok(room);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener habitacion: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener habitacion" });
        }
    }

    // --------------------------------------------------------
    // GET /api/rooms/available
    // Query requeridos en formato dd-MM-yyyy:
    //   ?checkIn=01-04-2026&checkOut=05-04-2026
    //   &roomNumber=201 (opcional, para verificar una habitacion especifica)
    //   &excludeReservationId=xxx (opcional, para excluir al modificar)
    // --------------------------------------------------------
    [HttpGet("available")]
    public async Task<IActionResult> GetAvailableRooms(
        [FromQuery] string checkIn,
        [FromQuery] string checkOut,
        [FromQuery] string? roomNumber = null,
        [FromQuery] string? excludeReservationId = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(checkIn) || string.IsNullOrWhiteSpace(checkOut))
                return BadRequest(new { message = "Las fechas son requeridas en formato dd-MM-yyyy." });

            DateTime checkInDate  = ParseFecha(checkIn);
            DateTime checkOutDate = ParseFecha(checkOut);

            if (checkInDate >= checkOutDate)
                return BadRequest(new { message = "La fecha de entrada debe ser anterior a la fecha de salida" });

            if (checkInDate < DateTime.UtcNow.Date)
                return BadRequest(new { message = "La fecha de entrada no puede ser en el pasado" });

            // Si se pasa roomNumber, verificar disponibilidad solo de esa habitacion
            if (!string.IsNullOrWhiteSpace(roomNumber))
            {
                var reservationsCol = _firebaseService.GetCollection("reservations");
                var roomQuery       = await reservationsCol
                    .WhereEqualTo("RoomNumber", roomNumber)
                    .GetSnapshotAsync();

                bool isAvailable = true;
                foreach (var doc in roomQuery.Documents)
                {
                    if (!string.IsNullOrWhiteSpace(excludeReservationId) && doc.Id == excludeReservationId)
                        continue;

                    var d      = doc.ToDictionary();
                    var status = d.ContainsKey("Status") ? d["Status"].ToString() : "confirmed";
                    if (status == "cancelled") continue;

                    var existingIn  = ((Google.Cloud.Firestore.Timestamp)d["CheckInDate"]).ToDateTime();
                    var existingOut = ((Google.Cloud.Firestore.Timestamp)d["CheckOutDate"]).ToDateTime();

                    if (checkInDate < existingOut && checkOutDate >= existingIn)
                    {
                        isAvailable = false;
                        break;
                    }
                }

                return Ok(new { available = isAvailable });
            }

            // Sin roomNumber: devolver todas las habitaciones disponibles
            var availableRooms = await _roomService.GetAvailableRooms(checkInDate, checkOutDate);
            int nights         = (int)(checkOutDate - checkInDate).TotalDays;

            var result = availableRooms.Select(r => new
            {
                r.Id,
                r.RoomNumber,
                r.RoomType,
                r.Capacity,
                r.Amenities,
                r.BaseRate,
                r.Description,
                r.PhotoUrl,
                nights,
                estimatedBase  = Math.Round(r.BaseRate * nights, 2),
                estimatedTax   = Math.Round(r.BaseRate * nights * 0.15, 2),
                estimatedTotal = Math.Round(r.BaseRate * nights * 1.15, 2)
            }).ToList();

            return Ok(new
            {
                checkIn,
                checkOut,
                nights,
                totalAvailable = result.Count,
                rooms = result
            });
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener habitaciones disponibles: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener habitaciones disponibles" });
        }
    }

    // --------------------------------------------------------
    // GET /api/rooms/search?term=suite
    // Disponible para todos
    // --------------------------------------------------------
    [HttpGet("search")]
    public async Task<IActionResult> SearchRooms([FromQuery] string term)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(term))
                return BadRequest(new { message = "El termino de busqueda es requerido" });

            var results = await _roomService.SearchRooms(term);
            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al buscar habitaciones: {ex.Message}");
            return StatusCode(500, new { message = "Error al buscar habitaciones" });
        }
    }

    // --------------------------------------------------------
    // GET /api/rooms/{roomNumber}/reviews
    // Disponible para todos — muestra resenas de una habitacion
    // Incluye promedio de estrellas y lista de comentarios
    // --------------------------------------------------------
    [HttpGet("{roomNumber}/reviews")]
    public async Task<IActionResult> GetReviews(string roomNumber)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(roomNumber))
                return BadRequest(new { message = "El numero de habitacion es requerido" });

            var reviewsCol = _firebaseService.GetCollection("reviews");
            // Nota: no combinamos WhereEqualTo con OrderByDescending en Firestore
            // porque requiere índice compuesto. Ordenamos en memoria en su lugar.
            var snapshot   = await reviewsCol
                .WhereEqualTo("RoomNumber", roomNumber)
                .GetSnapshotAsync();

            var reviews = snapshot.Documents.Select(doc =>
            {
                var d = doc.ToDictionary();
                var createdAtRaw = d.ContainsKey("CreatedAt")
                    ? ((Google.Cloud.Firestore.Timestamp)d["CreatedAt"]).ToDateTime()
                    : DateTime.MinValue;
                return new
                {
                    id            = doc.Id,
                    reservationId = d.ContainsKey("ReservationId") ? d["ReservationId"].ToString() : "",
                    userName      = d.ContainsKey("UserName")  ? d["UserName"].ToString()  : "",
                    stars         = d.ContainsKey("Stars")     ? Convert.ToInt32(d["Stars"]) : 0,
                    comment       = d.ContainsKey("Comment")   ? d["Comment"].ToString()   : "",
                    createdAt     = createdAtRaw != DateTime.MinValue
                        ? createdAtRaw.ToString("dd-MM-yyyy")
                        : "",
                    createdAtSort = createdAtRaw
                };
            })
            // Ordenar en memoria de más reciente a más antigua
            .OrderByDescending(r => r.createdAtSort)
            .Select(r => new { r.id, r.reservationId, r.userName, r.stars, r.comment, r.createdAt })
            .ToList();

            var averageStars = reviews.Count > 0
                ? Math.Round(reviews.Average(r => r.stars), 1)
                : 0.0;

            return Ok(new
            {
                roomNumber,
                totalReviews = reviews.Count,
                averageStars,
                reviews
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener resenas de habitacion {roomNumber}: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener resenas" });
        }
    }

    // --------------------------------------------------------
    // GET /api/rooms/all-reviews
    // Público — devuelve las últimas reseñas de todas las habitaciones
    // Usado en el landing page para mostrar testimonios reales
    // --------------------------------------------------------
    [HttpGet("all-reviews")]
    public async Task<IActionResult> GetAllReviews([FromQuery] int limit = 12)
    {
        try
        {
            var reviewsCol = _firebaseService.GetCollection("reviews");
            // Sin filtros: OrderByDescending simple funciona en Firestore sin índice compuesto
            var snapshot   = await reviewsCol.GetSnapshotAsync();

            var reviews = snapshot.Documents
                .Select(doc =>
                {
                    var d = doc.ToDictionary();
                    var createdAtRaw = d.ContainsKey("CreatedAt")
                        ? ((Google.Cloud.Firestore.Timestamp)d["CreatedAt"]).ToDateTime()
                        : DateTime.MinValue;
                    return new
                    {
                        id         = doc.Id,
                        roomNumber = d.ContainsKey("RoomNumber") ? d["RoomNumber"].ToString() : "",
                        userName   = d.ContainsKey("UserName")   ? d["UserName"].ToString()   : "Huesped",
                        stars      = d.ContainsKey("Stars")      ? Convert.ToInt32(d["Stars"]) : 0,
                        comment    = d.ContainsKey("Comment")    ? d["Comment"].ToString()    : "",
                        createdAt  = createdAtRaw != DateTime.MinValue
                            ? createdAtRaw.ToString("dd-MM-yyyy")
                            : "",
                        createdAtSort = createdAtRaw
                    };
                })
                .Where(r => r.stars > 0 && !string.IsNullOrWhiteSpace(r.comment))
                .OrderByDescending(r => r.createdAtSort)
                .Take(Math.Min(limit, 50))
                .Select(r => new { r.id, r.roomNumber, r.userName, r.stars, r.comment, r.createdAt })
                .ToList();

            return Ok(new { total = reviews.Count, reviews });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener todas las reseñas: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener reseñas" });
        }
    }

    // --------------------------------------------------------
    // DELETE /api/rooms/{roomNumber}/reviews/{reviewId}
    // Solo manager — elimina una reseña inapropiada
    // --------------------------------------------------------
    [HttpDelete("{roomNumber}/reviews/{reviewId}")]
    [Authorize]
    public async Task<IActionResult> DeleteReview(string roomNumber, string reviewId)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "manager")
                return StatusCode(403, new { message = "Solo el gerente puede eliminar reseñas" });

            if (string.IsNullOrWhiteSpace(reviewId))
                return BadRequest(new { message = "El ID de la reseña es requerido" });

            var reviewsCol = _firebaseService.GetCollection("reviews");
            var doc        = await reviewsCol.Document(reviewId).GetSnapshotAsync();

            if (!doc.Exists)
                return NotFound(new { message = "Reseña no encontrada" });

            // Verificar que la reseña pertenece a la habitación indicada
            var d = doc.ToDictionary();
            var docRoomNumber = d.ContainsKey("RoomNumber") ? d["RoomNumber"].ToString() : "";
            if (!string.IsNullOrWhiteSpace(docRoomNumber) && docRoomNumber != roomNumber)
                return BadRequest(new { message = "La reseña no pertenece a esta habitación" });

            await reviewsCol.Document(reviewId).DeleteAsync();

            _logger.LogInformation($"Reseña {reviewId} eliminada por el gerente (Hab. {roomNumber})");
            return Ok(new { message = "Reseña eliminada correctamente" });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al eliminar reseña {reviewId}: {ex.Message}");
            return StatusCode(500, new { message = "Error al eliminar la reseña" });
        }
    }

    // --------------------------------------------------------
    // POST /api/rooms/{roomNumber}/reviews
    // Solo huesped autenticado cuya estadía ya termino.
    // Solo puede dejar una resena por reserva.
    // Cuerpo: { "reservationId": "...", "stars": 4, "comment": "..." }
    // --------------------------------------------------------
    [HttpPost("{roomNumber}/reviews")]
    [Authorize]
    public async Task<IActionResult> CreateReview(string roomNumber, [FromBody] CreateReviewDto reviewDto)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "guest")
                return StatusCode(403, new { message = "Solo los huespedes pueden dejar resenas" });

            var userId = User.FindFirst("sub")?.Value;
            var userName = User.FindFirst("name")?.Value ?? "Huesped";

            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { message = "Token invalido" });

            if (reviewDto == null)
                return BadRequest(new { message = "El cuerpo de la peticion es requerido" });

            if (reviewDto.Stars < 1 || reviewDto.Stars > 5)
                return BadRequest(new { message = "La calificacion debe ser entre 1 y 5 estrellas" });

            if (string.IsNullOrWhiteSpace(reviewDto.ReservationId))
                return BadRequest(new { message = "El ID de la reserva es requerido" });

            var reservationsCol = _firebaseService.GetCollection("reservations");
            var reviewsCol      = _firebaseService.GetCollection("reviews");

            // Verificar que la reserva existe y pertenece al huesped
            var reservationDoc = await reservationsCol.Document(reviewDto.ReservationId).GetSnapshotAsync();
            if (!reservationDoc.Exists)
                return NotFound(new { message = "Reserva no encontrada" });

            var reservationDict = reservationDoc.ToDictionary();
            if (reservationDict["UserId"].ToString() != userId)
                return StatusCode(403, new { message = "No puedes resenyar una reserva que no es tuya" });

            // Verificar que la estadía ya termino
            var checkOutDate    = ((Google.Cloud.Firestore.Timestamp)reservationDict["CheckOutDate"]).ToDateTime();
            var hondurasNow     = DateTime.UtcNow.AddHours(-6);
            if (checkOutDate > hondurasNow)
                return BadRequest(new { message = "Solo puedes dejar una resena despues de que termine tu estadía" });

            // Verificar que no haya dejado ya una resena para esta reserva
            var existingReview = await reviewsCol
                .WhereEqualTo("ReservationId", reviewDto.ReservationId)
                .GetSnapshotAsync();

            if (existingReview.Count > 0)
                return BadRequest(new { message = "Ya dejaste una resena para esta estadía" });

            // Crear la resena
            var newReview = new Review
            {
                Id            = Guid.NewGuid().ToString(),
                RoomNumber    = roomNumber,
                UserId        = userId,
                UserName      = userName,
                ReservationId = reviewDto.ReservationId,
                Stars         = reviewDto.Stars,
                Comment       = reviewDto.Comment?.Trim() ?? "",
                CreatedAt     = DateTime.UtcNow
            };

            var reviewData = new Dictionary<string, object>
            {
                { "RoomNumber",    newReview.RoomNumber },
                { "UserId",        newReview.UserId },
                { "UserName",      newReview.UserName },
                { "ReservationId", newReview.ReservationId },
                { "Stars",         newReview.Stars },
                { "Comment",       newReview.Comment },
                { "CreatedAt",     newReview.CreatedAt }
            };

            await reviewsCol.Document(newReview.Id).SetAsync(reviewData);

            _logger.LogInformation($"Resena creada por {userId} para habitacion {roomNumber}: {reviewDto.Stars} estrellas");

            return Created($"/api/rooms/{roomNumber}/reviews", new
            {
                message = "Resena publicada exitosamente",
                reviewId = newReview.Id
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al crear resena: {ex.Message}");
            return StatusCode(500, new { message = "Error al publicar la resena" });
        }
    }

    // --------------------------------------------------------
    // POST /api/rooms
    // Solo gerente
    // --------------------------------------------------------
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomDto createRoomDto)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "manager")
                return StatusCode(403, new { message = "Solo el gerente puede crear habitaciones" });

            if (createRoomDto == null)
                return BadRequest(new { message = "El cuerpo de la peticion es requerido" });

            var managerId   = User.FindFirst("sub")?.Value ?? string.Empty;
            var managerName = User.FindFirst("name")?.Value ?? "Gerente";

            var room = await _roomService.CreateRoom(createRoomDto, managerName, managerId);

            _logger.LogInformation($"Habitacion creada: {room.RoomNumber} por {managerName}");

            return Created($"/api/rooms/{room.Id}", room);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al crear habitacion: {ex.Message}");
            return StatusCode(500, new { message = "Error al crear habitacion" });
        }
    }

    // --------------------------------------------------------
    // PUT /api/rooms/{roomNumber}
    // Solo gerente
    // --------------------------------------------------------
    [HttpPut("{roomNumber}")]
    [Authorize]
    public async Task<IActionResult> UpdateRoom(string roomNumber, [FromBody] UpdateRoomDto updateRoomDto)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "manager")
                return StatusCode(403, new { message = "Solo el gerente puede editar habitaciones" });

            if (string.IsNullOrWhiteSpace(roomNumber))
                return BadRequest(new { message = "El numero de habitacion es requerido" });

            if (updateRoomDto == null)
                return BadRequest(new { message = "El cuerpo de la peticion es requerido" });

            var roomId = await _roomService.GetRoomIdByNumber(roomNumber);
            if (roomId == null)
                return NotFound(new { message = $"No existe ninguna habitacion con el numero '{roomNumber}'" });

            var managerName = User.FindFirst("name")?.Value ?? "Gerente";
            var updatedRoom = await _roomService.UpdateRoom(roomId, updateRoomDto, managerName);

            _logger.LogInformation($"Habitacion {roomNumber} actualizada por {managerName}");

            return Ok(updatedRoom);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al actualizar habitacion: {ex.Message}");
            return StatusCode(500, new { message = "Error al actualizar habitacion" });
        }
    }

    // --------------------------------------------------------
    // DELETE /api/rooms/{roomNumber}
    // Solo gerente — falla si tiene reservas activas
    // --------------------------------------------------------
    [HttpDelete("{roomNumber}")]
    [Authorize]
    public async Task<IActionResult> DeleteRoom(string roomNumber)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "manager")
                return StatusCode(403, new { message = "Solo el gerente puede eliminar habitaciones" });

            if (string.IsNullOrWhiteSpace(roomNumber))
                return BadRequest(new { message = "El numero de habitacion es requerido" });

            var roomId = await _roomService.GetRoomIdByNumber(roomNumber);
            if (roomId == null)
                return NotFound(new { message = $"No existe ninguna habitacion con el numero '{roomNumber}'" });

            var managerName = User.FindFirst("name")?.Value ?? "Gerente";
            await _roomService.DeleteRoom(roomId);

            _logger.LogInformation($"Habitacion {roomNumber} eliminada por {managerName}");

            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al eliminar habitacion: {ex.Message}");
            return StatusCode(500, new { message = "Error al eliminar habitacion" });
        }
    }

    // --------------------------------------------------------
    // Parsea un string dd-MM-yyyy a DateTime UTC
    // --------------------------------------------------------
    private DateTime ParseFecha(string fecha)
    {
        var partes = fecha.Split('-');

        if (partes.Length != 3)
            throw new ArgumentException($"Formato invalido: '{fecha}'. Use dd-MM-yyyy.");

        if (!int.TryParse(partes[0], out int dia) ||
            !int.TryParse(partes[1], out int mes) ||
            !int.TryParse(partes[2], out int anio))
            throw new ArgumentException($"La fecha '{fecha}' contiene valores no numericos");

        try
        {
            return new DateTime(anio, mes, dia, 0, 0, 0, DateTimeKind.Utc);
        }
        catch
        {
            throw new ArgumentException($"La fecha '{fecha}' no es valida.");
        }
    }
}