using Google.Cloud.Firestore;

namespace Proyecto_Progra_Web.API.Models
{
    // Review representa una resena que un huesped deja sobre una habitacion
    // despues de que su estadía ha concluido.
    // Se guarda en la coleccion "reviews" de Firestore.
    [FirestoreData]
    public class Review
    {
        // Id del documento en Firestore
        public string Id { get; set; } = string.Empty;

        // Numero de la habitacion reseniada (ej: "101")
        [FirestoreProperty]
        public string RoomNumber { get; set; } = string.Empty;

        // Id del usuario que deja la resena
        [FirestoreProperty]
        public string UserId { get; set; } = string.Empty;

        // Nombre del huesped al momento de la resena
        [FirestoreProperty]
        public string UserName { get; set; } = string.Empty;

        // Id de la reserva asociada a esta resena (una resena por reserva)
        [FirestoreProperty]
        public string ReservationId { get; set; } = string.Empty;

        // Calificacion del 1 al 5 estrellas
        [FirestoreProperty]
        public int Stars { get; set; }

        // Comentario libre del huesped (opcional)
        [FirestoreProperty]
        public string Comment { get; set; } = string.Empty;

        // Fecha en que se dejo la resena
        [FirestoreProperty]
        public DateTime CreatedAt { get; set; }

        // Constructor vacio requerido por Firestore
        public Review() { }
    }
}