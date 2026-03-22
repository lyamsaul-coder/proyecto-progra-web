namespace Proyecto_Progra_Web.API.DTOs
{
    // UserDto es lo que se envia al frontend cuando se piden datos de un usuario
    // No incluye contrasena ni datos sensibles internos
    public class UserDto
    {
        // Identificador del usuario
        public string Id { get; set; } = string.Empty;

        // Nombre visible en el sistema
        public string Fullname { get; set; } = string.Empty;

        // Correo electronico del usuario
        public string Email { get; set; } = string.Empty;

        // Rol: "manager" o "guest"
        public string Role { get; set; } = string.Empty;

        // URL de foto de perfil
        public string ProfilePictureUrl { get; set; } = string.Empty;

        // Indica si ya realizo una reserva (relevante para el panel del huesped)
        public bool HasReserved { get; set; }

        // Id de la habitacion que reservo, null si no ha reservado
        public string? ReservedRoomId { get; set; }

        // Fechas de su reserva como texto legible, null si no ha reservado
        public string? ReservedDates { get; set; }

        // Indica si el usuario debe cambiar su contrasena al iniciar sesion.
        // El frontend lo usa para redirigir a change-password.html obligatoriamente.
        public bool RequiresPasswordChange { get; set; }
    }

    // RegisterDto es lo que recibe el backend cuando alguien se registra
    public class RegisterDto
    {
        // Correo electronico con el que se registra
        public string Email { get; set; } = string.Empty;

        // Contrasena (viaja cifrada por HTTPS, nunca se almacena en texto plano)
        public string Password { get; set; } = string.Empty;

        // Nombre completo que aparecera en su perfil
        public string Fullname { get; set; } = string.Empty;
    }

    // LoginDto es lo que recibe el backend cuando alguien inicia sesion
    public class LoginDto
    {
        // Correo electronico del usuario
        public string Email { get; set; } = string.Empty;

        // Contrasena del usuario
        public string Password { get; set; } = string.Empty;
    }

    // ForgotPasswordDto es lo que recibe el backend para verificar si el email existe
    public class ForgotPasswordDto
    {
        // Email de la cuenta a recuperar
        public string Email { get; set; } = string.Empty;
    }

    // ResetPasswordDto es lo que recibe el backend para actualizar la contrasena
    public class ResetPasswordDto
    {
        // Email de la cuenta a recuperar
        public string Email { get; set; } = string.Empty;

        // Nueva contrasena que reemplazara a la anterior
        public string NewPassword { get; set; } = string.Empty;
    }

    // ChangePasswordDto es lo que recibe el endpoint change-password
    // Requiere token JWT — solo el usuario autenticado puede cambiar su propia contrasena
    public class ChangePasswordDto
    {
        // Contrasena actual (requerida para cambio desde sesion activa)
        // Puede estar vacia solo cuando RequiresPasswordChange = true (contrasena temporal)
        public string CurrentPassword { get; set; } = string.Empty;

        // Nueva contrasena elegida por el usuario
        public string NewPassword { get; set; } = string.Empty;

        // Confirmacion — debe ser igual a NewPassword
        public string ConfirmPassword { get; set; } = string.Empty;
    }

    // AuthResponseDto es la respuesta que devuelve el backend al registrar o iniciar sesion
    // Contiene el token JWT y los datos basicos del usuario
    public class AuthResponseDto
    {
        // Indica si la operacion fue exitosa
        public bool Success { get; set; }

        // Mensaje descriptivo del resultado (exito o descripcion del error)
        public string Message { get; set; } = string.Empty;

        // Token JWT para autenticar futuras peticiones
        // Vacio si la autenticacion fallo
        public string Token { get; set; } = string.Empty;

        // Datos del usuario autenticado
        public UserDto User { get; set; } = new UserDto();
    }
}