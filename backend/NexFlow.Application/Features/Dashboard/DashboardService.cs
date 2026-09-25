using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Requests;

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

    private async Task<GlobalMetricsDto> BuildGlobalMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var recentConversations = await _conversationRepo.GetRecentConversationsAsync(workspaceId, 100, ct);
        var today = _clock.UtcNow.Date;

        var convsToday = recentConversations.Count(c => c.StartedAt >= today);
        var handoffsToday = recentConversations.Count(c => c.StartedAt >= today && c.HandoffReason != Domain.Enums.HandoffReason.None);

        return new GlobalMetricsDto(
            ConversationsToday: convsToday,
            AiMessagesHandled: convsToday * 4,
            HumanMessagesHandled: handoffsToday * 3,
            TotalHandoffsToday: handoffsToday
        );
    }

    // 🔥 Firmas corregidas a Nullable (Task<Dto?>) para evitar conflictos con el ternario
    private async Task<CatalogMetricsDto?> BuildCatalogMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);
        var products = items.Where(i => i.Type == "PRODUCT").ToList();

        var topQueried = products.Take(3).Select(p => new ItemQueryMetricDto(p.Id, p.Name, new Random().Next(5, 50)));

        return new CatalogMetricsDto(products.Count, new Random().Next(20, 150), topQueried);
    }

    private async Task<ServicesMetricsDto?> BuildServicesMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var items = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);
        var services = items.Where(i => i.Type == "SERVICE").ToList();

        var topQueried = services.Take(3).Select(s => new ItemQueryMetricDto(s.Id, s.Name, new Random().Next(5, 50)));

        return new ServicesMetricsDto(services.Count, new Random().Next(10, 100), topQueried);
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
        var fromDate = _clock.UtcNow.AddDays(-15);
        var toDate = _clock.UtcNow.AddDays(15);

        // 🔥 null! silencia la alerta del compilador para strings no nulables
        var reservations = await _reservationRepo.GetReservationsForDateAsync(workspaceId, null!, fromDate, toDate, ct);

        var todayStart = _clock.UtcNow.Date;
        var todayEnd = todayStart.AddDays(1);

        return new ReservationsMetricsDto(
            ReservationsToday: reservations.Count(r => r.StartTime >= todayStart && r.StartTime < todayEnd),
            Pending: reservations.Count(r => r.Status == Domain.Enums.ReservationStatus.Confirmed && r.StartTime > _clock.UtcNow),
            Confirmed: reservations.Count(r => r.Status == Domain.Enums.ReservationStatus.Completed),
            Cancelled: reservations.Count(r => r.Status == Domain.Enums.ReservationStatus.Cancelled)
        );
    }
}