using Proyecto_Progra_Web.API.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Proyecto_Progra_Web.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<TestController> _logger;
    private readonly ServerInstanceService _serverInstance;

    public TestController(
        FirebaseService firebaseService,
        ILogger<TestController> logger,
        ServerInstanceService serverInstance)
    {
        _firebaseService = firebaseService;
        _logger = logger;
        _serverInstance = serverInstance;
    }

    // GET /api/test/firebase
    [HttpGet("firebase")]
    public async Task<IActionResult> TestFirebaseConnection()
    {
        try
        {
            var testCollection = _firebaseService.GetCollection("test");
            var snapshot = await testCollection.Limit(1).GetSnapshotAsync();
            return Ok(new
            {
                success = true,
                message = "Conexion exitosa",
                documentInTestCollection = snapshot.Count,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { success = false, message = "Sin conexion", error = e.Message });
        }
    }

    // GET /api/test/health
    // Usado por el frontend en todas las paginas protegidas cada 30 segundos.
    // Ademas de verificar que la API responde, valida el claim "sid" del token
    // contra el InstanceId actual del servidor.
    // Si el servidor se reinicio, el InstanceId cambio y devuelve 401,
    // lo que hace que el frontend limpie la sesion y muestre el login.
    [HttpGet("health")]
    [Authorize]
    public IActionResult HealthCheck()
    {
        var tokenSid = User.FindFirst("sid")?.Value;

        if (string.IsNullOrEmpty(tokenSid) || tokenSid != _serverInstance.InstanceId)
        {
            return Unauthorized(new
            {
                message = "Sesion invalida: el servidor fue reiniciado. Por favor inicia sesion nuevamente."
            });
        }

        return Ok(new { status = "API Corriendo", timestamp = DateTime.UtcNow });
    }

    // GET /api/test/claims
    // Endpoint temporal de diagnostico: muestra todos los claims del token entrante
    // Enviar con Authorization: Bearer {token} en Swagger
    // Permite ver exactamente como ASP.NET lee los claims del JWT
    [HttpGet("claims")]
    [Authorize]
    public IActionResult GetClaims()
    {
        var claims = User.Claims.Select(c => new
        {
            type = c.Type,
            value = c.Value
        }).ToList();

        return Ok(new
        {
            isAuthenticated = User.Identity!.IsAuthenticated,
            identityName = User.Identity.Name,
            claims = claims
        });
    }
}