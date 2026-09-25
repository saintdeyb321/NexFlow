namespace NexFlow.Application.Features.Dashboard;

public record DashboardResponseDto(
    GlobalMetricsDto Global,
    CatalogMetricsDto? Catalog,
    ServicesMetricsDto? Services,
    ReservationsMetricsDto? Reservations,
    RequestsMetricsDto? Requests
);

// Métricas de atención global (Siempre presentes si tiene acceso al sistema)
public record GlobalMetricsDto(
    int ConversationsToday,
    int AiMessagesHandled,
    int HumanMessagesHandled,
    int TotalHandoffsToday
);

public record CatalogMetricsDto(
    int TotalProducts,
    int TotalQueriesThisWeek,
    IEnumerable<ItemQueryMetricDto> TopQueriedProducts
);

public record ServicesMetricsDto(
    int TotalServices,
    int TotalQueriesThisWeek,
    IEnumerable<ItemQueryMetricDto> TopQueriedServices
);

public record ItemQueryMetricDto(
    string Id,
    string Name,
    int Queries
);

public record ReservationsMetricsDto(
    int ReservationsToday,
    int Pending,
    int Confirmed,
    int Cancelled
);

public record RequestsMetricsDto(
    int Pending,
    int InReview,
    int Completed
);