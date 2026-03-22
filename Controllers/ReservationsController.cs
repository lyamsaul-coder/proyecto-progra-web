using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Google.Cloud.Firestore;

namespace Proyecto_Progra_Web.API.Controllers;

/// <summary>
/// ReservationsController maneja la creacion, consulta, cancelacion y modificacion de reservas.
///
/// Endpoints del huesped (rol "guest"):
///   POST /api/reservations                   - crear reserva
///   GET  /api/reservations/my                - ver su propia reserva
///   PUT  /api/reservations/{id}/cancel-guest  - cancelar su reserva (min 24h antes del check-in)
///   PUT  /api/reservations/{id}/modify        - modificar fechas (min 24h antes del check-in)
///
/// Endpoints del gerente (rol "manager"):
///   GET  /api/reservations                    - ver todas las reservas
///   PUT  /api/reservations/{id}/cancel         - cancelar cualquier reserva sin restriccion
///   PUT  /api/reservations/guests/{userId}/block - bloquear permanentemente un huesped
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ReservationsController : ControllerBase
{
    private readonly IReservationService _reservationService;
    private readonly ILogger<ReservationsController> _logger;
    private readonly FirebaseService _firebaseService;

    public ReservationsController(
        IReservationService reservationService,
        FirebaseService firebaseService,
        ILogger<ReservationsController> logger)
    {
        _reservationService = reservationService;
        _firebaseService    = firebaseService;
        _logger = logger;
    }

    // --------------------------------------------------------
    // GET /api/reservations
    // Solo gerente — devuelve todas las reservas
    // --------------------------------------------------------
    [HttpGet]
    [Authorize(Roles = "manager")]
    public async Task<IActionResult> GetAllReservations()
    {
        try
        {
            var reservations = await _reservationService.GetAllReservations();
            return Ok(reservations);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener reservas: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener reservas" });
        }
    }

    // --------------------------------------------------------
    // GET /api/reservations/my
    // Solo huesped autenticado — devuelve su propia reserva
    // --------------------------------------------------------
    [HttpGet("my")]
    [Authorize]
    public async Task<IActionResult> GetMyReservation()
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "guest")
                return StatusCode(403, new { message = "Solo los huespedes pueden acceder a este recurso" });

            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { message = "Token invalido o expirado" });

            var reservation = await _reservationService.GetReservationByUserId(userId);
            if (reservation == null)
                return NotFound(new { message = "No tienes ninguna reserva registrada" });

            return Ok(reservation);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener reserva del usuario: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener reserva" });
        }
    }

    // --------------------------------------------------------
    // POST /api/reservations
    // Solo huesped — crea una nueva reserva
    // --------------------------------------------------------
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateReservation([FromBody] CreateReservationDto createReservationDto)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "guest")
                return StatusCode(403, new { message = "Solo los huespedes pueden realizar reservas" });

            if (createReservationDto == null)
                return BadRequest(new { message = "El cuerpo de la peticion es requerido" });

            if (string.IsNullOrWhiteSpace(createReservationDto.RoomNumber))
                return BadRequest(new { message = "El numero de habitacion es requerido" });

            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { message = "Token invalido o expirado" });

            var response = await _reservationService.CreateReservation(createReservationDto, userId);

            _logger.LogInformation($"Reserva creada para usuario {userId}: {response.Reservation?.Id}");

            return Created($"/api/reservations/my", response);
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
            _logger.LogError($"Error al crear reserva: {ex.Message}");
            return StatusCode(500, new { message = "Error al procesar la reserva" });
        }
    }

    // --------------------------------------------------------
    // PUT /api/reservations/{id}/cancel
    // Solo gerente — cancela cualquier reserva sin restriccion de tiempo
    // Libera la habitacion y permite al huesped volver a reservar
    // --------------------------------------------------------
    [HttpPut("{id}/cancel")]
    [Authorize(Roles = "manager")]
    public async Task<IActionResult> CancelReservation(string id)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "El ID de la reserva es requerido" });

            await _reservationService.CancelReservation(id);

            _logger.LogInformation($"Reserva {id} cancelada por el gerente");

            return Ok(new { message = "Reserva cancelada exitosamente. El huesped puede volver a reservar." });
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
            _logger.LogError($"Error al cancelar reserva {id}: {ex.Message}");
            return StatusCode(500, new { message = "Error al cancelar la reserva" });
        }
    }

    // --------------------------------------------------------
    // PUT /api/reservations/{id}/cancel-guest
    // Solo huesped — cancela su propia reserva.
    // Requiere mas de 24 horas antes del check-in.
    // --------------------------------------------------------
    [HttpPut("{id}/cancel-guest")]
    [Authorize]
    public async Task<IActionResult> CancelReservationGuest(string id)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "guest")
                return StatusCode(403, new { message = "Solo los huespedes pueden usar este endpoint" });

            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { message = "Token invalido" });

            if (string.IsNullOrWhiteSpace(id))
                return BadRequest(new { message = "El ID de la reserva es requerido" });

            await _reservationService.CancelReservationByGuest(id, userId);

            _logger.LogInformation($"Huesped {userId} cancelo su reserva {id}");

            return Ok(new { message = "Reserva cancelada. Ya puedes realizar una nueva reserva." });
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
            _logger.LogError($"Error al cancelar reserva {id} por huesped: {ex.Message}");
            return StatusCode(500, new { message = "Error al cancelar la reserva" });
        }
    }

    // --------------------------------------------------------
    // PUT /api/reservations/{id}/modify
    // Solo huesped — modifica las fechas de su propia reserva.
    // Requiere mas de 24 horas antes del check-in actual.
    // Valida disponibilidad en las nuevas fechas automaticamente.
    // --------------------------------------------------------
    [HttpPut("{id}/modify")]
    [Authorize]
    public async Task<IActionResult> ModifyReservation(string id, [FromBody] ModifyReservationDto modifyDto)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "guest")
                return StatusCode(403, new { message = "Solo los huespedes pueden modificar sus reservas" });

            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { message = "Token invalido" });

            if (modifyDto == null ||
                string.IsNullOrWhiteSpace(modifyDto.CheckInDate) ||
                string.IsNullOrWhiteSpace(modifyDto.CheckOutDate))
                return BadRequest(new { message = "Las fechas son requeridas" });

            await _reservationService.ModifyReservation(id, userId, modifyDto.CheckInDate, modifyDto.CheckOutDate);

            _logger.LogInformation($"Huesped {userId} modifico fechas de reserva {id}");

            return Ok(new { message = "Fechas actualizadas correctamente." });
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
            _logger.LogError($"Error al modificar reserva {id}: {ex.Message}");
            return StatusCode(500, new { message = "Error al modificar la reserva" });
        }
    }

    // --------------------------------------------------------
    // PUT /api/reservations/guests/{userId}/block
    // Solo gerente — bloquea permanentemente la cuenta de un huesped
    // --------------------------------------------------------
    // --------------------------------------------------------
    // GET /api/reservations/my-history
    // Huesped autenticado — devuelve todo su historial de reservas
    // --------------------------------------------------------
    [HttpGet("my-history")]
    [Authorize]
    public async Task<IActionResult> GetMyHistory()
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "guest")
                return StatusCode(403, new { message = "Solo los huespedes pueden ver su historial" });

            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { message = "Token invalido" });

            var history = await _reservationService.GetReservationHistoryByUserId(userId);
            return Ok(new { total = history.Count, reservations = history });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener historial: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener historial" });
        }
    }

    // --------------------------------------------------------
    // POST /api/reservations/manager-create
    // Solo gerente — crea una reserva en nombre de un huesped
    // --------------------------------------------------------
    [HttpPost("manager-create")]
    [Authorize]
    public async Task<IActionResult> ManagerCreateReservation([FromBody] ManagerCreateReservationDto dto)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "manager")
                return StatusCode(403, new { message = "Solo el gerente puede crear reservas para otros huespedes" });

            if (dto == null || string.IsNullOrWhiteSpace(dto.GuestUserId))
                return BadRequest(new { message = "El ID del huesped es requerido" });

            var response = await _reservationService.CreateReservation(
                new CreateReservationDto
                {
                    RoomNumber   = dto.RoomNumber,
                    CheckInDate  = dto.CheckInDate,
                    CheckOutDate = dto.CheckOutDate
                },
                dto.GuestUserId
            );

            // Notificar al huesped que el manager le creó una reserva
            var notifCol = _firebaseService.GetCollection("notifications");
            await notifCol.Document(Guid.NewGuid().ToString()).SetAsync(new Dictionary<string, object>
            {
                { "UserId",    dto.GuestUserId },
                { "Type",      "reservation_created_by_manager" },
                { "Title",     "Nueva reserva confirmada" },
                { "Message",   $"El hotel te ha creado una reserva en Hab. {response.Reservation?.RoomNumber} ({response.Reservation?.CheckInDate} → {response.Reservation?.CheckOutDate})." },
                { "Icon",      "🏨" },
                { "Read",      false },
                { "CreatedAt", DateTime.UtcNow }
            });

            _logger.LogInformation($"Gerente creó reserva para huesped {dto.GuestUserId}: Hab. {dto.RoomNumber}");
            return Created("/api/reservations/manager-create", response);
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
            _logger.LogError($"Error en manager-create: {ex.Message}");
            return StatusCode(500, new { message = "Error al crear la reserva" });
        }
    }

    // --------------------------------------------------------
    // GET /api/reservations/{id}/has-review
    // Huesped autenticado — verifica si ya dejó reseña para esta reserva
    // Devuelve 200 si existe reseña, 404 si no
    // --------------------------------------------------------
    [HttpGet("{reservationId}/has-review")]
    [Authorize]
    public async Task<IActionResult> HasReview(string reservationId)
    {
        try
        {
            var userId = User.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(userId))
                return Unauthorized(new { message = "Token invalido" });

            var reviewsCol = _firebaseService.GetCollection("reviews");
            var snapshot   = await reviewsCol
                .WhereEqualTo("ReservationId", reservationId)
                .GetSnapshotAsync();

            if (snapshot.Count > 0)
                return Ok(new { hasReview = true, reservationId });

            return NotFound(new { hasReview = false });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al verificar reseña de reserva {reservationId}: {ex.Message}");
            return StatusCode(500, new { message = "Error al verificar reseña" });
        }
    }

    [HttpPut("guests/{userId}/block")]
    [Authorize(Roles = "manager")]
    public async Task<IActionResult> BlockGuest(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest(new { message = "El ID del huesped es requerido" });

            await _reservationService.BlockGuest(userId);

            _logger.LogInformation($"Huesped {userId} bloqueado por el gerente");

            return Ok(new { message = "Cuenta del huesped desactivada permanentemente." });
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
            _logger.LogError($"Error al bloquear huesped {userId}: {ex.Message}");
            return StatusCode(500, new { message = "Error al bloquear el huesped" });
        }
    }
}