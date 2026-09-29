namespace NexFlow.Domain.Enums;

public enum ConversationIntent
{
    General,
    ProductQuery,
    ServiceQuery,
    Reservation,
    Request,
    Order,
    Faq,
    Location,
    BusinessHours,
    Support,

    // 🔥 SPRINT 05: Intenciones exclusivas de sistema para manejar caídas y errores sin alucinar
    ProviderUnavailable,
    RateLimited
}