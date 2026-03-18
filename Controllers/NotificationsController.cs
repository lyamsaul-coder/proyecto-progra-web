using Proyecto_Progra_Web.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Proyecto_Progra_Web.API.Controllers;

/// <summary>
/// NotificationsController genera notificaciones segun el rol del usuario.
///
/// Gerente (manager):
///   GET /api/notifications  → Ultimas reservas registradas en el sistema
///
/// Huesped (guest):
///   GET /api/notifications  → Su propia reserva si existe (confirmacion)
///
/// El frontend hace polling a este endpoint cada N segundos y compara
/// los IDs recibidos con los que ya tiene en localStorage para detectar novedades.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IReservationService _reservationService;
    private readonly ILogger<NotificationsController> _logger;

    public NotificationsController(
        IReservationService reservationService,
        ILogger<NotificationsController> logger)
    {
        _reservationService = reservationService;
        _logger = logger;
    }

    // --------------------------------------------------------
    // GET /api/notifications
    // Header: Authorization: Bearer {token}
    //
    // Manager → devuelve las ultimas 20 reservas del sistema
    //           cada una se convierte en una notificacion
    //
    // Guest   → devuelve solo su propia reserva si existe
    //           (para notificar "tu reserva fue confirmada")
    //
    // Respuesta:
    // [
    //   {
    //     "id": "reservationId_abc123",
    //     "type": "new_reservation",          // manager
    //     "title": "Nueva reserva",
    //     "message": "Juan Pérez reservó Hab. 101 (3 noches)",
    //     "timestamp": "18-03-2026 14:30",
    //     "icon": "🏨"
    //   },
    //   ...
    // ]
    // --------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> GetNotifications()
    {
        try
        {
            var role      = User.FindFirst("role")?.Value;
            var userId    = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? User.FindFirst("sub")?.Value
                         ?? User.FindFirst("uid")?.Value;

            if (role == "manager")
            {
                // El gerente ve todas las reservas del sistema como notificaciones
                var reservations = await _reservationService.GetAllReservations();

                var notifications = reservations
                    .OrderByDescending(r => r.Timestamp)
                    .Take(20)
                    .Select(r => new
                    {
                        id        = $"res_{r.Id}",
                        type      = "new_reservation",
                        title     = "Nueva reserva",
                        message   = $"{r.UserName} reservó Hab. {r.RoomNumber} · {r.Nights} noche{(r.Nights == 1 ? "" : "s")} · {r.CheckInDate} → {r.CheckOutDate}",
                        timestamp = r.Timestamp,
                        icon      = "🏨"
                    })
                    .ToList();

                return Ok(notifications);
            }
            else if (role == "guest")
            {
                // El huesped solo ve la notificacion de su propia reserva
                if (string.IsNullOrEmpty(userId))
                    return Ok(new List<object>());

                var reservation = await _reservationService.GetReservationByUserId(userId);

                if (reservation == null)
                    return Ok(new List<object>());

                var notifications = new[]
                {
                    new
                    {
                        id        = $"res_{reservation.Id}",
                        type      = "reservation_confirmed",
                        title     = "¡Reserva confirmada!",
                        message   = $"Hab. {reservation.RoomNumber} ({reservation.RoomType}) · {reservation.CheckInDate} → {reservation.CheckOutDate} · {reservation.TotalCost:C2}",
                        timestamp = reservation.Timestamp,
                        icon      = "✅"
                    }
                };

                return Ok(notifications);
            }

            return Ok(new List<object>());
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener notificaciones: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener notificaciones" });
        }
    }
}