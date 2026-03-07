namespace Proyecto_Progra_Web.API.Services;

using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Models;
/// <summary>
/// IAuthService define los metodos necesarios para la autenticación
///
/// Responsibilidades
/// - Registrar nuevos usuarios
/// - Validar credenciales del login
/// - Generar tokens JWT
/// - Validad los tokens existentes
///
/// </summary>
public interface IAuthService
{
    /**
     * Registrar:
     * 1. Validar el correo y pass
     * 2. Verificar que el correo no este duplicado
     * 3. Crear usuario en Firebase Auth
     * 4. Guardar en el documento de User en FS
     * 5. Devolver respuesta del usuario creado
     *
     * Parametros:
     *   registerDto contiene el correo, pass, fullname
     *
     * Retorna:
     *  User: el usuario con sus propiedades y su ID
     *
     * Lanza excepciones:
     *   Email ya existe
     *   Password muy corto
     *   Error interno de FB
     */

    Task<User> Register(RegisterDto registerDto);

    /**
     *   Login:
     *   1. Validar que el email existe
     *   2. Verificar credenciales contra Firebase Auth
     *   3. Si son correctas, generar el JWT
     *   4. Actualizar los valores de login
     *   5. Devolver un token y datos del usuario
     *
     *   Parametro:
     *   loginDto: email, pass
     *
     *   Retornar
     *   tupla (User, token JWT)
     *
     *   Lanza excepcion:
     *   - Email no existe
     *   - Pass es incorrecto
     * 
     */
    Task<(User user, string token)> Login(LoginDto loginDto);

    Task<bool> ValidateToken(string token);

    Task<User?> GetUserById(string userId);

    string GenerateJwtToken(User user);

