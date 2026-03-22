namespace Proyecto_Progra_Web.API.DTOs
{
    // ReservationDto es lo que se envia cuando el gerente o el huesped consultan reservas
    // Incluye todos los datos del log de auditoria
    public class ReservationDto
    {
        // Identificador del documento en Firestore
        public string Id { get; set; } = string.Empty;

        // Id del usuario que reservo
        public string UserId { get; set; } = string.Empty;

        // Nombre del usuario al momento de la reserva
        public string UserName { get; set; } = string.Empty;

        // Id de la habitacion
        public string RoomId { get; set; } = string.Empty;

        // Numero de habitacion
        public string RoomNumber { get; set; } = string.Empty;

        // Tipo de habitacion
        public string RoomType { get; set; } = string.Empty;

        // Fecha de entrada en formato dd-MM-yyyy
        public string CheckInDate { get; set; } = string.Empty;

        // Fecha de salida en formato dd-MM-yyyy
        public string CheckOutDate { get; set; } = string.Empty;

        // Noches calculadas
        public int Nights { get; set; }

        // Costo total con impuestos
        public double TotalCost { get; set; }

        // Estado: "confirmed", "cancelled"
        public string Status { get; set; } = string.Empty;

        // Fecha y hora en que se realizo la reserva en formato dd-MM-yyyy HH:mm
        public string Timestamp { get; set; } = string.Empty;
    }

    // CreateReservationDto es lo que recibe el backend cuando el huesped quiere reservar
    // El huesped identifica la habitacion por su numero visible, no por el ID interno
    // Las fechas se reciben en formato dd-MM-yyyy
    public class CreateReservationDto
    {
        // Numero de habitacion visible para el huesped (ej: "504", "101")
        // El backend busca el ID interno a partir de este numero
        public string RoomNumber { get; set; } = string.Empty;

        // Fecha de entrada en formato dd-MM-yyyy
        public string CheckInDate { get; set; } = string.Empty;

        // Fecha de salida en formato dd-MM-yyyy
        public string CheckOutDate { get; set; } = string.Empty;
    }

    // ModifyReservationDto es lo que recibe el backend cuando el huesped modifica sus fechas
    // Solo contiene las nuevas fechas — el ID de la reserva va en la URL
    public class ModifyReservationDto
    {
        // Nueva fecha de entrada en formato dd-MM-yyyy
        public string CheckInDate { get; set; } = string.Empty;

        // Nueva fecha de salida en formato dd-MM-yyyy
        public string CheckOutDate { get; set; } = string.Empty;
    }

    // ReservationResponseDto es la respuesta que recibe el huesped al confirmar su reserva
    // Incluye el costo calculado con impuestos para mostrarlo en pantalla
    public class ReservationResponseDto
    {
        // Indica si la reserva se proceso correctamente
        public bool Success { get; set; }

        // Mensaje de confirmacion o descripcion del error
        public string Message { get; set; } = string.Empty;

        // Datos completos de la reserva creada
        public ReservationDto? Reservation { get; set; }

        // Desglose del costo para mostrar en la pantalla de confirmacion
        public double BaseAmount { get; set; }

        // Impuesto aplicado (porcentaje * base)
        public double TaxAmount { get; set; }

        // Total final = BaseAmount + TaxAmount
        public double TotalAmount { get; set; }
    }

    // ManagerCreateReservationDto: el gerente crea una reserva en nombre de un huesped
    public class ManagerCreateReservationDto
    {
        // ID del huesped para quien se crea la reserva
        public string GuestUserId { get; set; } = string.Empty;

        // Numero de habitacion visible
        public string RoomNumber { get; set; } = string.Empty;

        // Fecha de entrada en formato dd-MM-yyyy
        public string CheckInDate { get; set; } = string.Empty;

        // Fecha de salida en formato dd-MM-yyyy
        public string CheckOutDate { get; set; } = string.Empty;
    }

    // CreateReviewDto es lo que recibe el backend cuando un huesped deja una resena
    public class CreateReviewDto
    {
        // ID de la reserva asociada (una resena por reserva)
        public string ReservationId { get; set; } = string.Empty;

        // Calificacion del 1 al 5 estrellas
        public int Stars { get; set; }

        // Comentario opcional del huesped
        public string Comment { get; set; } = string.Empty;
    }
}