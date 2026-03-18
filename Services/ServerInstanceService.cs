namespace Proyecto_Progra_Web.API.Services;

/// <summary>
/// Genera un ID unico (GUID) cuando el servidor arranca.
/// Este ID se incluye en cada token JWT como el claim "sid" (server instance id).
///
/// Al reiniciar el servidor, se genera un nuevo GUID.
/// Cuando el frontend llama a /api/test/health, el endpoint compara
/// el "sid" del token con el GUID actual. Si no coinciden  401 cierra sesion.
///
/// Registrado como Singleton en Program.cs para que el mismo GUID
/// persista durante toda la vida del proceso.
/// </summary>
public class ServerInstanceService
{
    public string InstanceId { get; } = Guid.NewGuid().ToString();
}