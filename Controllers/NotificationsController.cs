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
    private readonly FirebaseService _firebaseService;

    public NotificationsController(
        IReservationService reservationService,
        FirebaseService firebaseService,
        ILogger<NotificationsController> logger)
    {
        _reservationService = reservationService;
        _firebaseService    = firebaseService;
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
                if (string.IsNullOrEmpty(userId))
                    return Ok(new List<object>());

                var notifList = new List<object>();

                // 1. Notificacion de reserva activa confirmada
                var reservation = await _reservationService.GetReservationByUserId(userId);
                if (reservation != null && reservation.Status != "cancelled")
                {
                    notifList.Add(new
                    {
                        id        = $"res_{reservation.Id}",
                        type      = "reservation_confirmed",
                        title     = "¡Reserva confirmada!",
                        message   = $"Hab. {reservation.RoomNumber} ({reservation.RoomType}) · {reservation.CheckInDate} → {reservation.CheckOutDate}",
                        timestamp = reservation.Timestamp,
                        icon      = "✅"
                    });
                }

                // 2. Notificaciones de sistema (cancelaciones por el manager, etc.)
                var notifCol  = _firebaseService.GetCollection("notifications");
                var snapshot  = await notifCol
                    .WhereEqualTo("UserId", userId)
                    .GetSnapshotAsync();

                foreach (var doc in snapshot.Documents)
                {
                    var d = doc.ToDictionary();
                    notifList.Add(new
                    {
                        id        = doc.Id,
                        type      = d.ContainsKey("Type")    ? d["Type"].ToString()    : "info",
                        title     = d.ContainsKey("Title")   ? d["Title"].ToString()   : "Notificacion",
                        message   = d.ContainsKey("Message") ? d["Message"].ToString() : "",
                        timestamp = d.ContainsKey("CreatedAt")
                            ? ((Google.Cloud.Firestore.Timestamp)d["CreatedAt"]).ToDateTime().ToString("dd-MM-yyyy HH:mm")
                            : "",
                        icon      = d.ContainsKey("Icon")    ? d["Icon"].ToString()    : "📌"
                    });
                }

                // Ordenar por más recientes primero
                return Ok(notifList.OrderByDescending(n => ((dynamic)n).timestamp).ToList());
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