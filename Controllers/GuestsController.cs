using Proyecto_Progra_Web.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Proyecto_Progra_Web.API.Controllers;

/// <summary>
/// GuestsController permite al gerente consultar la lista de huespedes
/// y el estado de sus reservas.
/// Todos los endpoints requieren rol "manager".
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class GuestsController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IReservationService _reservationService;
    private readonly ILogger<GuestsController> _logger;

    public GuestsController(
        IAuthService authService,
        IReservationService reservationService,
        ILogger<GuestsController> logger)
    {
        _authService = authService;
        _reservationService = reservationService;
        _logger = logger;
    }

    // --------------------------------------------------------
    // GET /api/guests
    // Solo gerente — lista de huespedes con estado de reserva
    // --------------------------------------------------------
    [HttpGet]
    public async Task<IActionResult> GetAllGuests()
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "manager")
                return StatusCode(403, new { message = "Solo el gerente puede ver la lista de huespedes" });

            var allGuests = await _authService.GetAllGuests();
            var allReservations = await _reservationService.GetAllReservations();

            // Diccionario rapido: userId -> reserva
            // Agrupar por usuario y tomar la reserva más reciente (evita crash con clave duplicada
            // cuando un huésped cancela y vuelve a reservar, generando múltiples registros)
            var reservationByUser = allReservations
                .GroupBy(r => r.UserId)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(r => r.Timestamp).First()
                );

            // Hora actual en Honduras (UTC-6) para comparar si una reserva ya expiro
            var hondurasNow = DateTime.UtcNow.AddHours(-6);

            var guestList = allGuests.Select(g =>
            {
                reservationByUser.TryGetValue(g.Id, out var reservation);

                // Determinar si la reserva ya expiro por fecha (checkOut < hoy Honduras)
                bool hasExpired = false;
                string effectiveStatus = reservation != null ? reservation.Status : "sin reservar";

                if (reservation != null && reservation.Status != "cancelled")
                {
                    // CheckOutDate viene como "dd-MM-yyyy" desde el DTO
                    if (DateTime.TryParseExact(
                            reservation.CheckOutDate,
                            "dd-MM-yyyy",
                            System.Globalization.CultureInfo.InvariantCulture,
                            System.Globalization.DateTimeStyles.None,
                            out var checkOutParsed))
                    {
                        // La reserva expira cuando checkOut (a medianoche UTC) ya paso
                        hasExpired = checkOutParsed.ToUniversalTime() < hondurasNow;
                        if (hasExpired)
                            effectiveStatus = "completed"; // estadía concluida
                    }
                }

                // Para el manager: un huesped "con reserva activa" es solo
                // aquel cuya reserva esta confirmada Y no ha expirado por fecha
                bool isActiveReservation = reservation != null
                    && reservation.Status != "cancelled"
                    && !hasExpired;

                return new
                {
                    userId = g.Id,
                    fullname = g.Fullname,
                    email = g.Email,
                    reservationStatus = effectiveStatus,
                    hasReserved = isActiveReservation,   // refleja el estado real, no el flag de Firestore
                    hasExpired,                          // para que el frontend pueda distinguir "completada"
                    // ID de la reserva — solo util si la reserva aun esta activa
                    reservationId = isActiveReservation ? reservation?.Id : null,
                    roomNumber = reservation?.RoomNumber,
                    roomType = reservation?.RoomType,
                    checkInDate = reservation?.CheckInDate,
                    checkOutDate = reservation?.CheckOutDate,
                    nights = reservation?.Nights,
                    totalCost = reservation?.TotalCost,
                    reservedAt = reservation?.Timestamp,
                    memberSince = g.CreatedAt
                };
            }).ToList();

            return Ok(new
            {
                totalGuests = guestList.Count,
                totalWithReservation = guestList.Count(g => g.hasReserved),
                totalWithoutReservation = guestList.Count(g => !g.hasReserved),
                guests = guestList
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener lista de huespedes: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener lista de huespedes" });
        }
    }

    // --------------------------------------------------------
    // GET /api/guests/{userId}
    // Solo gerente — datos completos del huesped y su reserva
    // --------------------------------------------------------
    [HttpGet("{userId}")]
    public async Task<IActionResult> GetGuestById(string userId)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "manager")
                return StatusCode(403, new { message = "Solo el gerente puede ver datos de huespedes" });

            if (string.IsNullOrWhiteSpace(userId))
                return BadRequest(new { message = "El ID del huesped es requerido" });

            var user = await _authService.GetUserById(userId);
            if (user == null)
                return NotFound(new { message = "Huesped no encontrado" });

            var reservation = await _reservationService.GetReservationByUserId(userId);

            return Ok(new
            {
                id = user.Id,
                fullname = user.Fullname,
                email = user.Email,
                hasReserved = user.HasReserved,
                reservedRoomId = user.ReservedRoomId,
                reservedDates = user.ReservedDates,
                reservationTimestamp = user.ReservationTimestamp,
                createdAt = user.CreatedAt,
                reservation = reservation
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener huesped {userId}: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener datos del huesped" });
        }
    }

    // --------------------------------------------------------
    // GET /api/guests/by-room/{roomNumber}
    // Solo gerente — devuelve el huesped y reserva activa de una habitacion
    // Usado cuando el manager hace clic en una habitacion en room-management
    // --------------------------------------------------------
    [HttpGet("by-room/{roomNumber}")]
    public async Task<IActionResult> GetGuestByRoom(string roomNumber)
    {
        try
        {
            var userRole = User.FindFirst("role")?.Value;
            if (userRole != "manager")
                return StatusCode(403, new { message = "Solo el gerente puede ver esta informacion" });

            if (string.IsNullOrWhiteSpace(roomNumber))
                return BadRequest(new { message = "El numero de habitacion es requerido" });

            // Obtener todas las reservas activas de esa habitacion (no canceladas y no expiradas)
            var hondurasNowRoom = DateTime.UtcNow.AddHours(-6);
            var allReservations = await _reservationService.GetAllReservations();
            var roomReservations = allReservations
                .Where(r => r.RoomNumber == roomNumber
                    && r.Status != "cancelled"
                    && DateTime.TryParseExact(r.CheckOutDate, "dd-MM-yyyy",
                        System.Globalization.CultureInfo.InvariantCulture,
                        System.Globalization.DateTimeStyles.None, out var co)
                    && co.ToUniversalTime() >= hondurasNowRoom)
                .OrderByDescending(r => r.Timestamp)
                .ToList();

            if (!roomReservations.Any())
                return Ok(new { hasReservation = false, reservations = new List<object>() });

            // Cruzar cada reserva con los datos del huesped
            var result = new List<object>();
            foreach (var res in roomReservations)
            {
                var user = await _authService.GetUserById(res.UserId);
                result.Add(new
                {
                    reservationId = res.Id,
                    userId = res.UserId,
                    guestName = res.UserName,
                    guestEmail = user?.Email ?? "",
                    roomNumber = res.RoomNumber,
                    roomType = res.RoomType,
                    checkInDate = res.CheckInDate,
                    checkOutDate = res.CheckOutDate,
                    nights = res.Nights,
                    totalCost = res.TotalCost,
                    status = res.Status,
                    reservedAt = res.Timestamp
                });
            }

            return Ok(new { hasReservation = true, reservations = result });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener huesped de habitacion {roomNumber}: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener datos de la habitacion" });
        }
    }
}