using Proyecto_Progra_Web.API.Models;
using Google.Cloud.Firestore;

namespace Proyecto_Progra_Web.API.Services;

/// <summary>
/// ReportService calcula estadisticas en tiempo real leyendo las colecciones
/// de Firestore. No almacena documentos de estadisticas;
/// de la peticion para garantizar datos actualizados segun lo indica el PDF.
/// </summary>
public class ReportService : IReportService
{
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<ReportService> _logger;

    public ReportService(FirebaseService firebaseService, ILogger<ReportService> logger)
    {
        _firebaseService = firebaseService;
        _logger = logger;
    }

    // --------------------------------------------------------
    // GET STATISTICS
    // --------------------------------------------------------

    public async Task<ReservationStatistics> GetStatistics(
        DateTime? periodStart = null,
        DateTime? periodEnd = null)
    {
        try
        {
            
            //Manejo de las diferencias de hora
            // Honduras esta en UTC-6
            // Para que "hoy" en Honduras sea correcto, ajustamos los limites del periodo
            // Si el usuario filtra del 17 al 18, en UTC eso es del 17 06:00 al 19 06:00
            const int hondurasOffsetHours = 6;

            var start = periodStart.HasValue
                ? periodStart.Value.Date.AddHours(hondurasOffsetHours)
                : DateTime.UtcNow.AddMonths(-1);

            var end = periodEnd.HasValue
                ? periodEnd.Value.Date.AddDays(1).AddHours(hondurasOffsetHours)
                : DateTime.UtcNow;

            var roomsCollection = _firebaseService.GetCollection("rooms");
            var reservationsCollection = _firebaseService.GetCollection("reservations");

            // Obtener todas las habitaciones registradas
            var roomsSnapshot = await roomsCollection.GetSnapshotAsync();

            // Obtener todas las reservas y filtrar en memoria por periodo
            // Firestore requiere indices compuestos para filtros multiples sobre el mismo campo
            var allReservationsSnapshot = await reservationsCollection.GetSnapshotAsync();

            var reservationsSnapshot = allReservationsSnapshot.Documents
                .Where(doc =>
                {
                    var d = doc.ToDictionary();
                    if (!d.ContainsKey("Timestamp")) return false;
                    var ts = ((Google.Cloud.Firestore.Timestamp)d["Timestamp"]).ToDateTime();
                    return ts >= start && ts <= end;
                })
                .ToList();

            var totalRooms = roomsSnapshot.Count;

            // Inicializar acumuladores
            int totalNights = 0;
            int confirmedReservations = 0;
            int pendingReservations = 0;
            double totalRevenue = 0.0;

            var reservationsByType = new Dictionary<string, int>();
            var revenueByType = new Dictionary<string, double>();

            // Acumulador para tendencia: clave = fecha, valor = cantidad de reservas
            var occupancyTrendRaw = new Dictionary<string, int>();

            foreach (var doc in reservationsSnapshot)
            {
                var dict = doc.ToDictionary();

                var nights = dict.ContainsKey("Nights") ? Convert.ToInt32(dict["Nights"]) : 0;
                var cost = dict.ContainsKey("TotalCost") ? Convert.ToDouble(dict["TotalCost"]) : 0.0;
                var roomType = dict.ContainsKey("RoomType") ? dict["RoomType"].ToString()! : "Desconocido";
                var status = dict.ContainsKey("Status") ? dict["Status"].ToString()! : "confirmed";
                var timestamp = dict.ContainsKey("Timestamp")
                    ? ((Timestamp)dict["Timestamp"]).ToDateTime()
                    : DateTime.UtcNow;

                // Acumular totales
                totalNights += nights;
                totalRevenue += cost;

                if (status == "confirmed")
                    confirmedReservations++;
                else
                    pendingReservations++;

                // Acumular por tipo de habitacion (para grafico de barras y circular)
                if (!reservationsByType.ContainsKey(roomType))
                {
                    reservationsByType[roomType] = 0;
                    revenueByType[roomType] = 0.0;
                }
                reservationsByType[roomType]++;
                revenueByType[roomType] += cost;

                // Acumular tendencia temporal por dia
                var dateKey = timestamp.ToString("yyyy-MM-dd");
                if (!occupancyTrendRaw.ContainsKey(dateKey))
                    occupancyTrendRaw[dateKey] = 0;
                occupancyTrendRaw[dateKey]++;
            }

            // Porcentaje de ocupacion: habitaciones que tienen al menos una reserva / total
            var roomsWithReservations = reservationsSnapshot
                .Select(d => d.ToDictionary()["RoomId"].ToString())
                .Distinct()
                .Count();

            double occupancyPercentage = totalRooms > 0
                ? Math.Round((double)roomsWithReservations / totalRooms * 100, 2)
                : 0;

            // Rellenar TODOS los dias del periodo con 0 si no tienen reservas
            // Esto garantiza que el grafico muestre el rango completo, no solo dias con datos
            var occupancyTrend = new Dictionary<string, int>();
            var currentDay = start.Date;
            while (currentDay <= end.Date)
            {
                var key = currentDay.ToString("yyyy-MM-dd");
                occupancyTrend[key] = occupancyTrendRaw.ContainsKey(key)
                    ? occupancyTrendRaw[key]
                    : 0;
                currentDay = currentDay.AddDays(1);
            }

            return new ReservationStatistics
            {
                TotalRooms = totalRooms,
                TotalNightsReserved = totalNights,
                OccupancyPercentage = occupancyPercentage,
                TotalRevenue = Math.Round(totalRevenue, 2),
                ConfirmedReservations = confirmedReservations,
                PendingReservations = pendingReservations,
                ReservationsByRoomType = reservationsByType,
                RevenueByRoomType = revenueByType,
                OccupancyTrend = occupancyTrend,
                PeriodStart = start,
                PeriodEnd = end,
                GeneratedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al generar estadisticas: {ex.Message}");
            throw;
        }
    }
}