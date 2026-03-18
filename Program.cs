using Proyecto_Progra_Web.API.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.IdentityModel.Tokens.Jwt;

// Deshabilitar el mapeo automatico de claims a nivel global
// Necesario para que el claim "role" no sea convertido al tipo URI largo de Microsoft
// y [Authorize(Roles = "manager")] funcione correctamente
JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();
JwtSecurityTokenHandler.DefaultOutboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// --------------------------------------------------------
// Registro de servicios
// --------------------------------------------------------

// FirebaseService como Singleton: una sola instancia para toda la app
// porque la conexion a Firestore es costosa de inicializar
builder.Services.AddSingleton<FirebaseService>();

// Los demas servicios como Scoped: una instancia por peticion HTTP
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IRoomService, RoomService>();
builder.Services.AddScoped<IReservationService, ReservationService>();
builder.Services.AddScoped<IReportService, ReportService>();

builder.Services.AddControllers();

// --------------------------------------------------------
// Configuracion de JWT
// --------------------------------------------------------
var jwtSettings = builder.Configuration.GetSection("Jwt");
var secretKey = Encoding.UTF8.GetBytes(jwtSettings["SecretKey"]!);

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    // Deshabilitar el mapeo de claims dentro del middleware JwtBearer
    // Esto evita que "role" se convierta al tipo URI largo de Microsoft
    options.MapInboundClaims = false;

    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(secretKey),
        RoleClaimType = "role",
        NameClaimType = "name"
    };
});

// --------------------------------------------------------
// Swagger con boton Authorize para enviar el token JWT
// --------------------------------------------------------
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ProBook API",
        Version = "v1",
        Description = "Sistema de Gestion Integrada de Reservas Hoteleras"
    });

    // Definicion del esquema de seguridad Bearer
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Ingrese el token JWT: Bearer {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });

    // Hacer que todos los endpoints bloqueados muestren el candado en Swagger
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// --------------------------------------------------------
// CORS: permitir peticiones desde el frontend Angular
// --------------------------------------------------------
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// --------------------------------------------------------
// Build y pipeline de la aplicacion
// --------------------------------------------------------
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("AllowAll");

// Servir archivos estaticos desde la carpeta wwwroot
// Permite acceder a los HTML desde https://localhost:puerto/login.html
app.UseStaticFiles();

// UseAuthentication debe ir antes de UseAuthorization
app.UseAuthentication();
app.UseAuthorization();

// Respuesta personalizada para 403 Forbidden
app.UseStatusCodePages(async context =>
{
    if (context.HttpContext.Response.StatusCode == 403)
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsync(
            "{\"message\":\"No tienes permiso para realizar esta accion\"}");
    }
});

app.MapControllers();
app.Run();