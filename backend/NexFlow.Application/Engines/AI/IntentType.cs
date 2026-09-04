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
    HumanHandoffRequest,

    Unknown,

    // 🔥 Auditoría (Sprint 1.2): Implementar salida para ambigüedades 
    // ("¿cuánto cuesta?") sin colapsar prematuramente en ServiceInformation.
    Ambiguous,

    // 🔥 Auditoría (Sprint 2.1): Fallback
    ProviderUnavailable
}