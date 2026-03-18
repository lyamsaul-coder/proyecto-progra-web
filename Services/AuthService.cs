using Proyecto_Progra_Web.API.DTOs;
using Proyecto_Progra_Web.API.Models;
using Google.Cloud.Firestore;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using BCrypt.Net;

namespace Proyecto_Progra_Web.API.Services;

/// <summary>
/// AuthService implementa el registro, login y manejo de tokens JWT.
/// Guarda y consulta usuarios en la coleccion "users" de Firestore.
/// </summary>
public class AuthService : IAuthService
{
    private readonly FirebaseService _firebaseService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    private readonly ServerInstanceService _serverInstance;

    public AuthService(
        FirebaseService firebaseService,
        IConfiguration configuration,
        ILogger<AuthService> logger,
        ServerInstanceService serverInstance)
    {
        _firebaseService = firebaseService;
        _configuration = configuration;
        _logger = logger;
        _serverInstance = serverInstance;
    }

    // --------------------------------------------------------
    // REGISTER
    // --------------------------------------------------------

    public async Task<User> Register(RegisterDto registerDto)
    {
        try
        {
            if (registerDto == null)
                throw new ArgumentException("El cuerpo de la peticion es requerido");

            if (string.IsNullOrWhiteSpace(registerDto.Email) ||
                string.IsNullOrWhiteSpace(registerDto.Password))
                throw new ArgumentException("Email y contrasena son requeridos");

            // Validar formato basico del email: debe contener @ y .com
            if (!registerDto.Email.Contains("@") || !registerDto.Email.Contains(".com"))
                throw new ArgumentException("El email debe tener un formato valido (ejemplo: usuario@correo.com)");

            // Validar formato de email: debe contener @ y .com
            if (!registerDto.Email.Contains("@") || !registerDto.Email.Contains(".com"))
                throw new ArgumentException("El email debe tener un formato valido (ejemplo: nombre@correo.com)");

            // Validar formato de email con expresion regular
            // Debe tener texto, arroba, texto y punto con dominio
            var emailRegex = new System.Text.RegularExpressions.Regex(
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$"
            );
            if (!emailRegex.IsMatch(registerDto.Email))
                throw new ArgumentException("El formato del correo electronico no es valido");

            if (registerDto.Password.Length < 6)
                throw new ArgumentException("La contrasena debe tener al menos 6 caracteres");

            var usersCollection = _firebaseService.GetCollection("users");

            // Verificar que el email no este registrado previamente
            var existingQuery = await usersCollection
                .WhereEqualTo("Email", registerDto.Email)
                .GetSnapshotAsync();

            if (existingQuery.Count > 0)
                throw new InvalidOperationException("El email ya esta registrado");

            // Hashear la contrasena, nunca se almacena en texto plano
            var passwordHash = BCrypt.Net.BCrypt.HashPassword(registerDto.Password);

            var newUser = new User
            {
                Id = Guid.NewGuid().ToString(),
                Email = registerDto.Email,
                Fullname = registerDto.Fullname,
                // Segun el PDF, todos los registros son "guest" por defecto
                Role = "guest",
                ProfilePictureUrl = string.Empty,
                HasReserved = false,
                ReservedRoomId = null,
                ReservedDates = null,
                ReservationTimestamp = null,
                CreatedAt = DateTime.UtcNow,
                LastLogin = DateTime.UtcNow,
                IsActive = true
            };

            // Guardar con Dictionary para incluir el hash que no esta en el modelo
            var userData = new Dictionary<string, object>
            {
                { "Id", newUser.Id },
                { "Email", newUser.Email },
                { "Fullname", newUser.Fullname },
                { "Role", newUser.Role },
                { "ProfilePictureUrl", newUser.ProfilePictureUrl },
                { "HasReserved", newUser.HasReserved },
                { "CreatedAt", newUser.CreatedAt },
                { "LastLogin", newUser.LastLogin },
                { "IsActive", newUser.IsActive },
                { "PasswordHash", passwordHash }
            };

            await usersCollection.Document(newUser.Id).SetAsync(userData);

            _logger.LogInformation($"Usuario registrado: {newUser.Email}");
            return newUser;
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning($"Validacion en Register: {ex.Message}");
            throw;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning($"Error logico en Register: {ex.Message}");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error inesperado en Register: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // LOGIN
    // --------------------------------------------------------

    public async Task<(User user, string token)> Login(LoginDto loginDto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(loginDto.Email) ||
                string.IsNullOrWhiteSpace(loginDto.Password))
                throw new ArgumentException("Email y contrasena son requeridos");

            // Validar formato basico del email
            if (!loginDto.Email.Contains("@") || !loginDto.Email.Contains(".com"))
                throw new ArgumentException("El email debe tener un formato valido (ejemplo: usuario@correo.com)");

            // Validar formato de email
            if (!loginDto.Email.Contains("@") || !loginDto.Email.Contains(".com"))
                throw new ArgumentException("El email debe tener un formato valido (ejemplo: nombre@correo.com)");

            // Validar formato de email
            var emailRegex = new System.Text.RegularExpressions.Regex(
                @"^[^@\s]+@[^@\s]+\.[^@\s]+$"
            );
            if (!emailRegex.IsMatch(loginDto.Email))
                throw new ArgumentException("El formato del correo electronico no es valido");

            var usersCollection = _firebaseService.GetCollection("users");

            var query = await usersCollection
                .WhereEqualTo("Email", loginDto.Email)
                .GetSnapshotAsync();

            // Mensaje generico para no revelar si el email existe o no
            if (query.Count == 0)
                throw new InvalidOperationException("Email o contrasena incorrectos");

            var userDoc = query.Documents[0];
            var userDict = userDoc.ToDictionary();

            var passwordHash = userDict["PasswordHash"].ToString()!;

            if (!BCrypt.Net.BCrypt.Verify(loginDto.Password, passwordHash))
                throw new InvalidOperationException("Email o contrasena incorrectos");

            var user = MapDictionaryToUser(userDict);

            var token = GenerateJwtToken(user);

            // Actualizar LastLogin en Firestore
            await usersCollection.Document(user.Id).UpdateAsync(
                new Dictionary<string, object>
                {
                    { "LastLogin", DateTime.UtcNow }
                }
            );

            _logger.LogInformation($"Login exitoso: {user.Email}");
            return (user, token);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en Login: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // VALIDATE TOKEN
    // --------------------------------------------------------

    public async Task<bool> ValidateToken(string token)
    {
        try
        {
            var secretKey = _configuration["Jwt:SecretKey"];
            if (string.IsNullOrEmpty(secretKey))
                return false;

            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.ASCII.GetBytes(secretKey);

            tokenHandler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            }, out SecurityToken validatedToken);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning($"Token invalido: {ex.Message}");
            return false;
        }
    }

    // --------------------------------------------------------
    // GET USER BY ID
    // --------------------------------------------------------

    public async Task<User?> GetUserById(string userId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(userId))
                return null;

            var usersCollection = _firebaseService.GetCollection("users");
            var doc = await usersCollection.Document(userId).GetSnapshotAsync();

            if (!doc.Exists)
                return null;

            return MapDictionaryToUser(doc.ToDictionary());
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener usuario {userId}: {ex.Message}");
            return null;
        }
    }

    // --------------------------------------------------------
    // GENERATE JWT TOKEN
    // --------------------------------------------------------

    public string GenerateJwtToken(User user)
    {
        try
        {
            var secretKey = _configuration["Jwt:SecretKey"];
            var issuer = _configuration["Jwt:Issuer"];
            var audience = _configuration["Jwt:Audience"];

            if (string.IsNullOrEmpty(secretKey))
                throw new InvalidOperationException("JWT SecretKey no configurado en appsettings");

            var key = Encoding.ASCII.GetBytes(secretKey);

            // Deshabilitar el mapeo automatico de claims
            // Sin esto, JwtSecurityTokenHandler convierte "role" al tipo URI largo
            // de Microsoft, rompiendo la validacion con [Authorize(Roles = "manager")]
            JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
            JwtSecurityTokenHandler.DefaultOutboundClaimTypeMap.Clear();

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim("sub", user.Id),
                    new Claim("email", user.Email),
                    new Claim("name", user.Fullname),
                    // El claim "role" se usa en los controllers para validar permisos
                    new Claim("role", user.Role),
                    // sid = server instance id: se genera al arrancar el servidor.
                    // Si el servidor se reinicia, este GUID cambia y el endpoint
                    // /api/test/health devuelve 401, forzando cierre de sesion en el frontend.
                    new Claim("sid", _serverInstance.InstanceId)
                }),
                Expires = DateTime.UtcNow.AddMinutes(15),
                Issuer = issuer,
                Audience = audience,
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(key),
                    SecurityAlgorithms.HmacSha256Signature)
            };

            var tokenHandler = new JwtSecurityTokenHandler();
            var token = tokenHandler.CreateToken(tokenDescriptor);

            return tokenHandler.WriteToken(token);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al generar token: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // GET ALL GUESTS
    // --------------------------------------------------------

    public async Task<List<User>> GetAllGuests()
    {
        try
        {
            var usersCollection = _firebaseService.GetCollection("users");

            // Filtrar unicamente los usuarios con rol "guest"
            var query = await usersCollection
                .WhereEqualTo("Role", "guest")
                .GetSnapshotAsync();

            var guests = new List<User>();
            foreach (var doc in query.Documents)
            {
                var user = MapDictionaryToUser(doc.ToDictionary());
                guests.Add(user);
            }

            return guests;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener lista de huespedes: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // FORGOT PASSWORD
    // --------------------------------------------------------

    public async Task<bool> ForgotPassword(string email)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("El email es requerido");

            var usersCollection = _firebaseService.GetCollection("users");

            var query = await usersCollection
                .WhereEqualTo("Email", email)
                .GetSnapshotAsync();

            // Devolver true si el email existe, false si no
            // No se revela informacion adicional por seguridad
            return query.Count > 0;
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en ForgotPassword: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // RESET PASSWORD
    // --------------------------------------------------------

    public async Task ResetPassword(string email, string newPassword)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException("El email es requerido");

            if (string.IsNullOrWhiteSpace(newPassword))
                throw new ArgumentException("La nueva contrasena es requerida");

            if (newPassword.Length < 6)
                throw new ArgumentException("La contrasena debe tener al menos 6 caracteres");

            var usersCollection = _firebaseService.GetCollection("users");

            var query = await usersCollection
                .WhereEqualTo("Email", email)
                .GetSnapshotAsync();

            if (query.Count == 0)
                throw new InvalidOperationException("No existe una cuenta con ese email");

            var userDoc = query.Documents[0];
            var userId = userDoc.ToDictionary()["Id"].ToString()!;

            // Hashear la nueva contrasena antes de guardarla
            var newPasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);

            await usersCollection.Document(userId).UpdateAsync(
                new Dictionary<string, object>
                {
                    { "PasswordHash", newPasswordHash }
                }
            );

            _logger.LogInformation($"Contrasena actualizada para: {email}");
        }
        catch (ArgumentException)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en ResetPassword: {ex.Message}");
            throw;
        }
    }

    // --------------------------------------------------------
    // METODO PRIVADO: MapDictionaryToUser
    // Convierte el Dictionary de Firestore a objeto User
    // --------------------------------------------------------

    private User MapDictionaryToUser(Dictionary<string, object> dict)
    {
        var user = new User
        {
            Id = dict["Id"].ToString()!,
            Email = dict["Email"].ToString()!,
            Fullname = dict["Fullname"].ToString()!,
            Role = dict["Role"].ToString()!,
            ProfilePictureUrl = dict.ContainsKey("ProfilePictureUrl")
                ? dict["ProfilePictureUrl"].ToString()!
                : string.Empty,
            HasReserved = dict.ContainsKey("HasReserved") && (bool)dict["HasReserved"],
            ReservedRoomId = dict.ContainsKey("ReservedRoomId") && dict["ReservedRoomId"] != null
                ? dict["ReservedRoomId"].ToString()
                : null,
            ReservedDates = dict.ContainsKey("ReservedDates") && dict["ReservedDates"] != null
                ? dict["ReservedDates"].ToString()
                : null,
            CreatedAt = dict.ContainsKey("CreatedAt")
                ? ((Timestamp)dict["CreatedAt"]).ToDateTime()
                : DateTime.UtcNow,
            LastLogin = dict.ContainsKey("LastLogin")
                ? ((Timestamp)dict["LastLogin"]).ToDateTime()
                : DateTime.UtcNow,
            IsActive = dict.ContainsKey("IsActive") && (bool)dict["IsActive"]
        };

        // ReservationTimestamp es nullable, solo se asigna si existe en el documento
        if (dict.ContainsKey("ReservationTimestamp") && dict["ReservationTimestamp"] != null)
            user.ReservationTimestamp = ((Timestamp)dict["ReservationTimestamp"]).ToDateTime();

        return user;
    }
}