namespace NexFlow.Domain.Enums;

public enum SenderType
{
    Consumer = 1,     // El cliente de WhatsApp
    AI = 2,           // El bot de NexFlow
    BusinessUser = 3, // El dueño respondiendo desde el Inbox o su propio teléfono
    System = 4        // Notificaciones automáticas (ej: "Reserva confirmada")
}