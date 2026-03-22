using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Models;

namespace Proyecto_Progra_Web.API.Services;

/// <summary>
/// IReservationService define los metodos para gestionar reservas.
///
/// Responsabilidades:
/// - Crear reservas validando disponibilidad
/// - Consultar reservas para el panel del gerente y del huesped
/// - Cancelar reservas (gerente sin restriccion, huesped con 24h de anticipacion)
/// - Modificar fechas de reserva con validacion de disponibilidad
/// - Bloquear huespedes problematicos
/// </summary>
public interface IReservationService
{
    // Crear una nueva reserva para el huesped autenticado
    // Lanza InvalidOperationException si el huesped ya reservo o la habitacion no esta disponible
    Task<ReservationResponseDto> CreateReservation(CreateReservationDto createReservationDto, string userId);

    // Obtener todas las reservas (uso exclusivo del gerente para reportes y auditoria)
    Task<List<ReservationDto>> GetAllReservations();

    // Obtener la reserva del huesped autenticado (un huesped solo puede ver la suya)
    // Devuelve null si el huesped no ha reservado
    Task<ReservationDto?> GetReservationByUserId(string userId);

    // Verificar si una habitacion esta disponible en un rango de fechas
    // Se usa antes de confirmar la reserva para evitar overbooking
    Task<bool> IsRoomAvailable(string roomId, DateTime checkIn, DateTime checkOut);

    // Calcular el costo total de una estadia: tarifa base * noches * impuesto
    double CalculateTotalCost(double baseRate, int nights, double taxRate = 0.15);

    // Cancelar una reserva activa (uso del gerente, sin restriccion de tiempo):
    // - Cambia el Status a "cancelled"
    // - Libera la habitacion (decrementa ReservationCount)
    // - Permite al huesped volver a reservar (HasReserved = false)
    Task CancelReservation(string reservationId);

    // Cancelar la propia reserva del huesped (solo con mas de 24h antes del check-in)
    // Verifica que la reserva pertenece al userId indicado antes de cancelar
    Task CancelReservationByGuest(string reservationId, string userId);

    // Modificar las fechas de una reserva existente
    // Requiere mas de 24h antes del check-in actual y valida disponibilidad en las nuevas fechas
    // Excluye la propia reserva al verificar solapamiento para no bloquearse a si misma
    Task ModifyReservation(string reservationId, string userId, string newCheckIn, string newCheckOut);

    // Bloquear permanentemente la cuenta de un huesped (IsActive = false)
    // El huesped no podra iniciar sesion. Usar solo para casos graves.
    Task BlockGuest(string userId);

    // Obtener todo el historial de reservas de un huesped (para el panel del huesped)
    // Devuelve todas sus reservas ordenadas de más reciente a más antigua
    Task<List<ReservationDto>> GetReservationHistoryByUserId(string userId);
}