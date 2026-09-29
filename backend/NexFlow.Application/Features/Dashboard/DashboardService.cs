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

        return new DashboardResponseDto(
            await BuildGlobalMetricsAsync(workspaceId, cancellationToken),
            modules.Contains("CATALOG") ? await BuildCatalogMetricsAsync(workspaceId, cancellationToken) : null,
            modules.Contains("SERVICES") ? await BuildServicesMetricsAsync(workspaceId, cancellationToken) : null,
            modules.Contains("RESERVATIONS") ? await BuildReservationsMetricsAsync(workspaceId, cancellationToken) : null,
            modules.Contains("REQUESTS") ? await BuildRequestsMetricsAsync(workspaceId, cancellationToken) : null);
    }

    private async Task<TimeZoneInfo> GetWorkspaceTimeZoneAsync(Guid workspaceId, CancellationToken ct)
    {
        var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
        var tzId = string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone;
        try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
    }

    private async Task<(DateTime Start, DateTime End)> TodayAsync(Guid workspaceId, CancellationToken ct)
    {
        var zone = await GetWorkspaceTimeZoneAsync(workspaceId, ct);
        var local = TimeZoneInfo.ConvertTimeFromUtc(_clock.UtcNow, zone);
        return (TimeZoneInfo.ConvertTimeToUtc(local.Date, zone), TimeZoneInfo.ConvertTimeToUtc(local.Date.AddDays(1), zone));
    }

    private async Task<GlobalMetricsDto> BuildGlobalMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var period = await TodayAsync(workspaceId, ct);
        var counts = await _conversationRepo.CountForPeriodAsync(workspaceId, period.Start, period.End, ct);
        return new GlobalMetricsDto(counts.Conversations, null, null, counts.Handoffs);
    }

    private async Task<CatalogMetricsDto?> BuildCatalogMetricsAsync(Guid workspaceId, CancellationToken ct) =>
        new(await _catalogRepo.CountActiveItemsByTypeAsync(workspaceId, "PRODUCT", ct), null, null);

    private async Task<ServicesMetricsDto?> BuildServicesMetricsAsync(Guid workspaceId, CancellationToken ct) =>
        new(await _catalogRepo.CountActiveItemsByTypeAsync(workspaceId, "SERVICE", ct), null, null);

    private async Task<RequestsMetricsDto?> BuildRequestsMetricsAsync(Guid workspaceId, CancellationToken ct) =>
        new(await _requestRepo.CountByStatusAsync(workspaceId, RequestStatus.Pending, ct),
            await _requestRepo.CountByStatusAsync(workspaceId, RequestStatus.InReview, ct),
            await _requestRepo.CountByStatusAsync(workspaceId, RequestStatus.Completed, ct));

    private async Task<ReservationsMetricsDto?> BuildReservationsMetricsAsync(Guid workspaceId, CancellationToken ct)
    {
        var today = await TodayAsync(workspaceId, ct);
        var counts = await _reservationRepo.CountForPeriodAsync(workspaceId, _clock.UtcNow.AddDays(-15), _clock.UtcNow.AddDays(15), today.Start, today.End, ct);
        return new ReservationsMetricsDto(counts.Today, null, counts.Confirmed, counts.Cancelled);
    }
}
