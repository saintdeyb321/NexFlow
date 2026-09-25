namespace NexFlow.Domain.Enums;

public enum OrderStatus
{
    PendingReview,  // La IA tomó el pedido, espera confirmación humana
    Approved,       // El negocio aceptó el pedido
    Processing,     // Preparando el paquete
    Completed,      // Entregado/Finalizado
    Rejected,       // Cancelado por falta de stock o por el cliente
    Cancelled
}