namespace NexFlow.Application.Engines.Intent.AI;

public enum IntentType
{
    // --- Operaciones (Reservas) ---
    CreateReservation,
    CheckAvailability,
    CancelReservation,

    // --- Solicitudes y Trámites ---
    CreateRequest,
    CheckRequestStatus,

    // --- Consultas Comerciales ---
    ServiceInformation,
    ProductInformation,

    // --- Base de Conocimiento e Identidad ---
    FaqQuery,
    BusinessProfileQuery,
    LocationQuery,
    BusinessHoursQuery,

    // --- Generales ---
    GeneralGreeting,
    HumanHandoffRequest, // Cuando el cliente explícitamente pide hablar con un humano
    Unknown
}