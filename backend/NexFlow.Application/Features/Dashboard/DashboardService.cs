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
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly IClock _clock;

    public DashboardService(
        IEntitlementService entitlementService,
        IConversationRepository conversationRepo,
        ICatalogRepository catalogRepo,
        IRequestRepository requestRepo,
        IReservationRepository reservationRepo,
        IBusinessProfileRepository profileRepo,
        IClock clock)
    {
        _entitlementService = entitlementService;
        _conversationRepo = conversationRepo;
        _catalogRepo = catalogRepo;
        _requestRepo = requestRepo;
        _reservationRepo = reservationRepo;
        _profileRepo = profileRepo;
        _clock = clock;
    }

    public async Task<DashboardResponseDto> GetDashboardSummaryAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken);
        var modules = activeModules.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var globalTask = BuildGlobalMetricsAsync(workspaceId, cancellationToken);
        var catalogTask = modules.Contains("CATALOG") ? BuildCatalogMetricsAsync(workspaceId, cancellationToken) : Task.FromResult<CatalogMetricsDto?>(null);
        var servicesTask = modules.Contains("SERVICES") ? BuildServicesMetricsAsync(workspaceId, cancellationToken) : Task.FromResult<ServicesMetricsDto?>(null);
        var requestsTask = modules.Contains("REQUESTS") ? BuildRequestsMetricsAsync(workspaceId, cancellationToken) : Task.FromResult<RequestsMetricsDto?>(null);
        var reservationsTask = modules.Contains("RESERVATIONS") ? BuildReservationsMetricsAsync(workspaceId, cancellationToken) : Task.FromResult<ReservationsMetricsDto?>(null);

        await Task.WhenAll(globalTask, catalogTask, servicesTask, requestsTask, reservationsTask);

        return new DashboardResponseDto(
            Global: globalTask.Result,
            Catalog: catalogTask.Result,
            Services: servicesTask.Result,
            Reservations: reservationsTask.Result,
            Requests: requestsTask.Result
        );
    }

    private async Task<TimeZoneInfo> GetWorkspaceTimeZoneAsync(Guid workspaceId, CancellationToken ct)
    {
        var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
        var tzId = string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone;
        try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
    }

    private async Task<GlobalMetricsDto> BuildGlobalMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        // 🔥 SPRINT 13: Eliminamos el destructivo N+1 de Mensajes en Firestore.
        // Solo contamos las Conversaciones del día sin cargar su sub-colección de mensajes.
        var recentConversations = await _conversationRepo.GetRecentConversationsAsync(workspaceId, 200, ct);

        var workspaceZone = await GetWorkspaceTimeZoneAsync(workspaceId, ct);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, workspaceZone);
        var todayLocalStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, workspaceZone);
        var tomorrowLocalStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date.AddDays(1), workspaceZone);

        var convsToday = recentConversations.Where(c => c.StartedAt >= todayLocalStartUtc && c.StartedAt < tomorrowLocalStartUtc).ToList();

        int totalConversations = convsToday.Count;
        int totalHandoffs = convsToday.Count(c => c.HandoffReason != HandoffReason.None);

        // Los contadores de mensajes individuales serán 0 hasta que implementes la nueva API
        // de Eventos (IMetricsRepository) sugerida por la Auditoría. Esto protege la RAM.
        return new GlobalMetricsDto(
            ConversationsToday: totalConversations,
            AiMessagesHandled: 0,
            HumanMessagesHandled: 0,
            TotalHandoffsToday: totalHandoffs
        );
    }

    private async Task<CatalogMetricsDto?> BuildCatalogMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);
        var products = items.Where(i => i.Type == "PRODUCT").ToList();

        var topQueried = Enumerable.Empty<ItemQueryMetricDto>();
        return new CatalogMetricsDto(products.Count, 0, topQueried);
    }

    private async Task<ServicesMetricsDto?> BuildServicesMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);
        var services = items.Where(i => i.Type == "SERVICE").ToList();

        var topQueried = Enumerable.Empty<ItemQueryMetricDto>();
        return new ServicesMetricsDto(services.Count, 0, topQueried);
    }

    private async Task<RequestsMetricsDto?> BuildRequestsMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var requests = await _requestRepo.GetRequestsAsync(workspaceId, 500, null, ct);

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

        var workspaceZone = await GetWorkspaceTimeZoneAsync(workspaceId, ct);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, workspaceZone);
        var todayLocalStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date, workspaceZone);
        var tomorrowLocalStartUtc = TimeZoneInfo.ConvertTimeToUtc(localNow.Date.AddDays(1), workspaceZone);

        // 🔥 SPRINT 07 FIX: Pending fue removido del sistema. Retornamos 0.
        return new ReservationsMetricsDto(
            ReservationsToday: reservations.Count(r => r.StartTime >= todayLocalStartUtc && r.StartTime < tomorrowLocalStartUtc),
            Pending: 0,
            Confirmed: countsByStatus.GetValueOrDefault(ReservationStatus.Confirmed),
            Cancelled: countsByStatus.GetValueOrDefault(ReservationStatus.Cancelled)
        );
    }
}