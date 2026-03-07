namespace Proyecto_Progra_Web.API.Models;

public class ReservationStatistics
{
    public string Id { get; set; } = string.Empty;

    public int TotalReservations { get; set; }

    public int ActiveReservations { get; set; }

    public int CancelledReservations { get; set; }

    public int CompletedReservations { get; set; }

    public decimal TotalRevenue { get; set; }

    public decimal AverageStayDuration { get; set; } // en días

    public string MostBookedRoomId { get; set; } = string.Empty;

    public string MostActiveUserId { get; set; } = string.Empty;

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    public DateTime GeneratedAt { get; set; }
    
    //Prueba de commit
}