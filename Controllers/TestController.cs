using Proyecto_Progra_Web.API.Services;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Mvc;
using Timestamp = Google.Cloud.Firestore.Timestamp;

namespace Proyecto_Progra_Web.API.Controllers;

/// <summary>
/// Controller de prueba para verificar que Firebase funciona correctamente
/// Una confirmado, podemos eliminar este controlador
/// </summary>

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly FirebaseService _firebaseService;
    private readonly ILogger<TestController> _logger;

    /*
     * Constructor: ASP.NET Core inyecta FirabaseService automaticamente
     * Porque necesitamos registrarlo en el program
     */

    public TestController(FirebaseService firebaseService, ILogger<TestController> logger)
    {
        _firebaseService = firebaseService;
        _logger = logger;
    }

    /*
     * Endpoint de prueba: GET /api/test/firebase
     * Intenta conectarse a Firebase y devuelve una respuesta / resultado
     */
    [HttpGet("firebase")]
    public async Task<IActionResult> TestFirebaseConnection()
    {
        try
        {
            _logger.LogInformation("Iniciando prueba de conexion a FB...");

            //Obtener la coleccion "test" (la creamos despues)
            var testCollection = _firebaseService.GetCollection("test");

            // Intentar leer un documento
            var snapshot = await testCollection.Limit(1).GetSnapshotAsync();

            //SI la conexion funciona
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
            _logger.LogError($"Error en prueba: {e.Message}");
            return StatusCode(500, new
            {
                success = false,
                message = "No conexion en el servidor",
                error = e.Message
            });
        }
    }
    /*
     * Endpoint de prueba simple: GET /api/test/health
     * Valida si la API esta corriendo
     */

    [HttpGet("health")]
    public IActionResult HealthCheck()
    {
        return Ok(new
        {
            status = "API Corriendo",
            timestamp = DateTime.UtcNow
        });
    }
}