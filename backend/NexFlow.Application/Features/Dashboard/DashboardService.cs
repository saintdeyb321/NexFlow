using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Requests;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Dashboard;

public interface IDashboardService
{
    Task<DashboardResponseDto> GetDashboardSummaryAsync(Guid workspaceId, CancellationToken cancellationToken);
}

public class DashboardService : IDashboardService
{
    private readonly IEntitlementService _entitlementService;
    private readonly IConversationRepository _conversationRepo;
    private readonly ICatalogRepository _catalogRepo;
    private readonly IRequestRepository _requestRepo;
    private readonly IReservationRepository _reservationRepo;
    private readonly IClock _clock;

    public DashboardService(
        IEntitlementService entitlementService,
        IConversationRepository conversationRepo,
        ICatalogRepository catalogRepo,
        IRequestRepository requestRepo,
        IReservationRepository reservationRepo,
        IClock clock)
    {
        _entitlementService = entitlementService;
        _conversationRepo = conversationRepo;
        _catalogRepo = catalogRepo;
        _requestRepo = requestRepo;
        _reservationRepo = reservationRepo;
        _clock = clock;
    }

    public async Task<DashboardResponseDto> GetDashboardSummaryAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken);
        var modules = activeModules.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // 🔥 SPRINT 11: Ejecución asíncrona paralela SOLO de los módulos contratados
        var globalTask = BuildGlobalMetricsAsync(workspaceId, cancellationToken);
        var catalogTask = modules.Contains("CATALOG") ? BuildCatalogMetricsAsync(workspaceId, cancellationToken) : Task.FromResult<CatalogMetricsDto?>(null);
        var servicesTask = modules.Contains("SERVICES") ? BuildServicesMetricsAsync(workspaceId, cancellationToken) : Task.FromResult<ServicesMetricsDto?>(null);
        var requestsTask = modules.Contains("REQUESTS") ? BuildRequestsMetricsAsync(workspaceId, cancellationToken) : Task.FromResult<RequestsMetricsDto?>(null);
        var reservationsTask = modules.Contains("RESERVATIONS") ? BuildReservationsMetricsAsync(workspaceId, cancellationToken) : Task.FromResult<ReservationsMetricsDto?>(null);

        // Opcional: Si implementaste Orders, podrías agregarlo aquí. Si no, lo omitimos.

        await Task.WhenAll(globalTask, catalogTask, servicesTask, requestsTask, reservationsTask);

        return new DashboardResponseDto(
            Global: globalTask.Result,
            Catalog: catalogTask.Result,
            Services: servicesTask.Result,
            Reservations: reservationsTask.Result,
            Requests: requestsTask.Result
        );
    }

    private async Task<GlobalMetricsDto> BuildGlobalMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var recentConversations = await _conversationRepo.GetRecentConversationsAsync(workspaceId, 200, ct);

        // 🔥 SPRINT 10: Cálculo de "Hoy" basado en la zona horaria local, no en UTC universal.
        var peruZone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, peruZone);
        var todayLocalStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, peruZone);
        var tomorrowLocalStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date.AddDays(1), peruZone);

        var convsToday = recentConversations.Where(c => c.StartedAt >= todayLocalStartUtc && c.StartedAt < tomorrowLocalStartUtc).ToList();

        int totalConversations = convsToday.Count;
        int totalHandoffs = convsToday.Count(c => c.HandoffReason != HandoffReason.None);

        int aiMessages = 0;
        int humanMessages = 0;

        foreach (var conv in convsToday)
        {
            var msgs = await _conversationRepo.GetMessagesAsync(workspaceId, conv.Id, 100, ct);
            aiMessages += msgs.Count(m => m.Sender == SenderType.AI);
            humanMessages += msgs.Count(m => m.Sender == SenderType.BusinessUser);
        }

        return new GlobalMetricsDto(
            ConversationsToday: totalConversations,
            AiMessagesHandled: aiMessages,
            HumanMessagesHandled: humanMessages,
            TotalHandoffsToday: totalHandoffs
        );
    }

    private async Task<CatalogMetricsDto?> BuildCatalogMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);
        var products = items.Where(i => i.Type == "PRODUCT").ToList();

        // 🔥 SPRINT 12: Extraemos el uso real si lo tuvieras, de lo contrario usamos un placeholder estático (NO RANDOM) 
        // para no romper la UI hasta que implementemos la tabla de CatalogAnalytics.
        var topQueried = products.Take(3).Select(p => new ItemQueryMetricDto(p.Id, p.Name, 0));

        return new CatalogMetricsDto(products.Count, 0, topQueried);
    }

    private async Task<ServicesMetricsDto?> BuildServicesMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);
        var services = items.Where(i => i.Type == "SERVICE").ToList();

        // 🔥 SPRINT 12: Mismo approach estático temporal.
        var topQueried = services.Take(3).Select(s => new ItemQueryMetricDto(s.Id, s.Name, 0));

        return new ServicesMetricsDto(services.Count, 0, topQueried);
    }

    private async Task<RequestsMetricsDto?> BuildRequestsMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var requests = await _requestRepo.GetRequestsAsync(workspaceId, ct);

        return new RequestsMetricsDto(
            Pending: requests.Count(r => r.Status == RequestStatus.Pending),
            InReview: requests.Count(r => r.Status == RequestStatus.InReview),
            Completed: requests.Count(r => r.Status == RequestStatus.Completed)
        );
    }

    private async Task<ReservationsMetricsDto?> BuildReservationsMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var fromDate = now.AddDays(-15);
        var toDate = now.AddDays(15);

        var reservations = (await _reservationRepo.GetReservationsForDateAsync(workspaceId, string.Empty, fromDate, toDate, ct)).ToList();

        var countsByStatus = reservations.GroupBy(r => r.Status)
            .ToDictionary(group => group.Key, group => group.Count());

        // 🔥 SPRINT 10: Ajuste de límites diarios locales para reservas
        var peruZone = TimeZoneInfo.FindSystemTimeZoneById("America/Lima");
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, peruZone);
        var todayLocalStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, peruZone);
        var tomorrowLocalStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date.AddDays(1), peruZone);

        return new ReservationsMetricsDto(
            ReservationsToday: reservations.Count(r => r.StartTime >= todayLocalStartUtc && r.StartTime < tomorrowLocalStartUtc),
            Pending: reservations.Count(r => r.Status == ReservationStatus.Confirmed && r.StartTime > now),
            Confirmed: countsByStatus.GetValueOrDefault(ReservationStatus.Completed),
            Cancelled: countsByStatus.GetValueOrDefault(ReservationStatus.Cancelled)
        );
    }
}