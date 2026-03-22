using Google.Cloud.Firestore;

namespace Proyecto_Progra_Web.API.Models
{
    // FirestoreData indica a la libreria que este objeto puede ser serializado/deserializado
    // desde y hacia un documento de Firestore
    [FirestoreData]
    public class User
    {
        // Id interno generado por Firestore
        // No lleva [FirestoreProperty] porque se asigna desde el Id del documento, no del campo
        public string Id { get; set; } = string.Empty;

        // Correo electronico unico del usuario
        [FirestoreProperty]
        public string Email { get; set; } = string.Empty;

        // Nombre completo visible en el sistema
        [FirestoreProperty]
        public string Fullname { get; set; } = string.Empty;

        // Rol del usuario: "manager" o "guest"
        // Por defecto los nuevos registros son "guest" segun el PDF
        [FirestoreProperty]
        public string Role { get; set; } = "guest";

        // URL de la foto de perfil almacenada en Firebase Storage
        [FirestoreProperty]
        public string ProfilePictureUrl { get; set; } = string.Empty;

        // Indica si el huesped ya realizo una reserva
        // Una vez en true, el usuario no puede volver a reservar (bloqueo permanente)
        [FirestoreProperty]
        public bool HasReserved { get; set; } = false;

        // Id de la habitacion que reservo (null si no ha reservado)
        [FirestoreProperty]
        public string? ReservedRoomId { get; set; }

        // Fechas de la reserva que realizo almacenadas como string (null si no ha reservado)
        // Formato: "checkIn - checkOut"
        [FirestoreProperty]
        public string? ReservedDates { get; set; }

        // Timestamp exacto de cuando realizo la reserva para auditoria
        [FirestoreProperty]
        public DateTime? ReservationTimestamp { get; set; }

        // Fecha en que se creo el usuario en el sistema
        [FirestoreProperty]
        public DateTime CreatedAt { get; set; }

        // Ultima vez que el usuario inicio sesion
        [FirestoreProperty]
        public DateTime LastLogin { get; set; }

        // Indica si la cuenta esta activa o fue deshabilitada
        [FirestoreProperty]
        public bool IsActive { get; set; } = true;

        // Indica si el usuario debe cambiar su contrasena al iniciar sesion.
        // Se activa en true cuando se le asigna una contrasena temporal via reset.
        // Se desactiva en false cuando el usuario la cambia desde change-password.html.
        [FirestoreProperty]
        public bool RequiresPasswordChange { get; set; } = false;

        // Constructor vacio requerido por Firestore para deserializar documentos
        public User() { }
    }
}