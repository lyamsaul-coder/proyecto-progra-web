namespace Proyecto_Progra_Web.API.Services;

using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Models;

/// <summary>
/// IAuthService define los metodos necesarios para la autenticacion de usuarios.
///
/// Responsabilidades:
/// - Registrar nuevos usuarios (rol "guest" por defecto segun el PDF)
/// - Validar credenciales en el login
/// - Generar y validar tokens JWT
/// - Consultar usuarios por ID
/// </summary>
public interface IAuthService
{
    // Registrar un nuevo usuario en Firestore
    // Lanza ArgumentException si el email ya existe o el password es muy corto
    Task<User> Register(RegisterDto registerDto);

    // Autenticar un usuario y devolver sus datos junto con el token JWT
    // Lanza InvalidOperationException si las credenciales son incorrectas
    Task<(User user, string token)> Login(LoginDto loginDto);

    // Verificar si un token JWT es valido y no ha expirado
    Task<bool> ValidateToken(string token);

    // Obtener los datos de un usuario por su ID de Firestore
    // Devuelve null si no existe
    Task<User?> GetUserById(string userId);

    // Generar un token JWT firmado para el usuario dado
    string GenerateJwtToken(User user);

    // Obtener todos los usuarios con rol "guest" registrados en el sistema
    Task<List<User>> GetAllGuests();

    // Verificar que el email existe en el sistema
    // Devuelve true si existe, false si no
    Task<bool> ForgotPassword(string email);

    // Asignar una contrasena temporal e indicar que el usuario debe cambiarla
    // al iniciar sesion (activa RequiresPasswordChange = true en Firestore)
    Task ResetPassword(string email, string newPassword);

    // Cambiar la contrasena desde dentro de una sesion activa
    // Desactiva RequiresPasswordChange despues de guardar
    Task<bool> VerifyCurrentPassword(string userId, string currentPassword);
    Task ChangePassword(string userId, string email, string newPassword);
}