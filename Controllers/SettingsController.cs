using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Proyecto_Progra_Web.API.Services;
using Google.Cloud.Firestore;

namespace Proyecto_Progra_Web.API.Controllers;

/// <summary>
/// SettingsController gestiona la configuración global del hotel.
/// GET  /api/settings/currency  — público (el frontend lo necesita sin login)
/// PUT  /api/settings/currency  — solo manager
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SettingsController : ControllerBase
{
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<SettingsController> _logger;

    public SettingsController(FirebaseService firebaseService, ILogger<SettingsController> logger)
    {
        _firebaseService = firebaseService;
        _logger          = logger;
    }

    // --------------------------------------------------------
    // GET /api/settings/currency
    // Público — devuelve la configuración de moneda activa
    // --------------------------------------------------------
    [HttpGet("currency")]
    public async Task<IActionResult> GetCurrencySettings()
    {
        try
        {
            var settingsCol = _firebaseService.GetCollection("settings");
            var doc         = await settingsCol.Document("currency").GetSnapshotAsync();

            if (!doc.Exists)
            {
                // Valores por defecto: Lempiras hondureños
                return Ok(new
                {
                    symbol    = "L.",
                    code      = "HNL",
                    locale    = "es-HN",
                    taxRate   = 0.15,
                    name      = "Lempira hondureño"
                });
            }

            var d = doc.ToDictionary();
            return Ok(new
            {
                symbol  = d.ContainsKey("Symbol")  ? d["Symbol"].ToString()            : "L.",
                code    = d.ContainsKey("Code")    ? d["Code"].ToString()              : "HNL",
                locale  = d.ContainsKey("Locale")  ? d["Locale"].ToString()            : "es-HN",
                taxRate = d.ContainsKey("TaxRate") ? Convert.ToDouble(d["TaxRate"])    : 0.15,
                name    = d.ContainsKey("Name")    ? d["Name"].ToString()              : "Lempira hondureño"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener configuración de moneda: {ex.Message}");
            return StatusCode(500, new { message = "Error al obtener configuración" });
        }
    }

    // --------------------------------------------------------
    // PUT /api/settings/currency
    // Solo manager — actualiza la configuración de moneda
    // --------------------------------------------------------
    [HttpPut("currency")]
    [Authorize]
    public async Task<IActionResult> UpdateCurrencySettings([FromBody] CurrencySettingsDto dto)
    {
        try
        {
            var role = User.FindFirst("role")?.Value;
            if (role != "manager")
                return StatusCode(403, new { message = "Solo el gerente puede cambiar la configuración" });

            if (dto == null)
                return BadRequest(new { message = "Datos requeridos" });

            if (string.IsNullOrWhiteSpace(dto.Symbol))
                return BadRequest(new { message = "El símbolo de moneda es requerido" });

            if (dto.TaxRate < 0 || dto.TaxRate > 1)
                return BadRequest(new { message = "La tasa de impuesto debe estar entre 0 y 1 (ej: 0.15 para 15%)" });

            var settingsCol = _firebaseService.GetCollection("settings");
            await settingsCol.Document("currency").SetAsync(new Dictionary<string, object>
            {
                { "Symbol",    dto.Symbol.Trim() },
                { "Code",      dto.Code?.Trim()   ?? "" },
                { "Locale",    dto.Locale?.Trim() ?? "es-HN" },
                { "TaxRate",   dto.TaxRate },
                { "Name",      dto.Name?.Trim()   ?? "" },
                { "UpdatedAt", DateTime.UtcNow }
            });

            _logger.LogInformation($"Configuración de moneda actualizada: {dto.Symbol} ({dto.Code}), ISV {dto.TaxRate * 100}%");

            return Ok(new
            {
                message = "Configuración actualizada correctamente",
                symbol  = dto.Symbol,
                code    = dto.Code,
                taxRate = dto.TaxRate
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al actualizar configuración de moneda: {ex.Message}");
            return StatusCode(500, new { message = "Error al actualizar configuración" });
        }
    }
}

public class CurrencySettingsDto
{
    public string Symbol  { get; set; } = "L.";
    public string Code    { get; set; } = "HNL";
    public string Locale  { get; set; } = "es-HN";
    public double TaxRate { get; set; } = 0.15;
    public string Name    { get; set; } = "Lempira hondureño";
}