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

    // 🔥 Auditoría (Sprint 2.1): Distingue cuando Gemini colapsa (503) para aplicar un fallback inteligente 
    // en lugar de enviar respuestas genéricas o asumir que el cliente habló mal.
    ProviderUnavailable
}