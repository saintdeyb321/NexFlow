using System.Diagnostics;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Reservations;

namespace NexFlow.Application.Features.Knowledge;

public enum BusinessReadStatus { Found, NotFound, Unavailable, Denied, Invalid, LimitReached }
public abstract record BusinessReadRequest;
public sealed record KnowledgeRead(KnowledgeTopic Topic, string? LocationId = null, string? SearchTerm = null,
    int Offset = 0, int Limit = 5) : BusinessReadRequest;
public sealed record AvailabilityRead(string LocationId, string ServiceId, DateTime LocalDate,
    TimeOnly? LocalTime = null) : BusinessReadRequest;

// Created by the authenticated backend, never deserialized from provider arguments.
public sealed class BusinessReadTurn
{
    public Guid WorkspaceId { get; }
    private readonly long _started = Stopwatch.GetTimestamp();
    private int _calls;
    public int Calls => Volatile.Read(ref _calls);
    internal BusinessReadTurn(Guid workspace) => WorkspaceId = workspace != Guid.Empty ? workspace : throw new ArgumentException("Workspace required.");
    internal TimeSpan Remaining => TimeSpan.FromSeconds(8) - Stopwatch.GetElapsedTime(_started);
    internal bool Acquire() => Interlocked.Increment(ref _calls) <= 3 && Remaining > TimeSpan.Zero;
}

public sealed record BusinessReadResult(Guid WorkspaceId, BusinessReadStatus Status, string Source,
    string? LocationId, IReadOnlyList<VerifiedFact> Facts, KnowledgeResult? Knowledge = null,
    IReadOnlyList<TimeSlotDto>? Slots = null, string? Reason = null);

public interface IBusinessReadTools
{
    BusinessReadTurn Open(Guid authenticatedWorkspace);
    Task<BusinessReadResult> ReadAsync(BusinessReadTurn turn, BusinessReadRequest request, CancellationToken ct);
}

// Bounded read facade; orchestration, interpretation and business effects remain in the existing services.
public sealed class BusinessReadTools(IKnowledgeService knowledge, IEntitlementService entitlements,
    IReservationEngine? reservations = null) : IBusinessReadTools
{
    public BusinessReadTurn Open(Guid authenticatedWorkspace) => new(authenticatedWorkspace);

    public async Task<BusinessReadResult> ReadAsync(BusinessReadTurn turn, BusinessReadRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var source = request is KnowledgeRead k ? k.Topic.ToString() : "Availability";
        var scope = request is KnowledgeRead q ? q.LocationId : (request as AvailabilityRead)?.LocationId;
        BusinessReadResult Empty(BusinessReadStatus status, string? reason = null) => new(turn.WorkspaceId, status, source, scope, [], Reason: reason);
        if (request is KnowledgeRead query && (!Enum.IsDefined(query.Topic) || query.Offset is < 0 or > 500 || query.Limit is < 1 or > 5 || query.SearchTerm?.Length > 200)
            || request is AvailabilityRead at && (string.IsNullOrWhiteSpace(at.LocationId) || string.IsNullOrWhiteSpace(at.ServiceId)
                || at.LocalDate.Kind == DateTimeKind.Utc) || request is not (KnowledgeRead or AvailabilityRead))
            return Empty(BusinessReadStatus.Invalid);
        if (!turn.Acquire()) return Empty(BusinessReadStatus.LimitReached);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(turn.Remaining > TimeSpan.Zero ? turn.Remaining : TimeSpan.FromMilliseconds(1));
        try
        {
            async Task<bool> Allowed() {
                var modules = (await entitlements.GetAvailableModuleCodesAsync(turn.WorkspaceId, deadline.Token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                return request switch {
                    KnowledgeRead { Topic: KnowledgeTopic.Profile } => true,
                    KnowledgeRead { Topic: KnowledgeTopic.Products or KnowledgeTopic.Offerings } => modules.Contains("CATALOG"),
                    KnowledgeRead { Topic: KnowledgeTopic.Services } => modules.Contains("SERVICES"),
                    KnowledgeRead { Topic: KnowledgeTopic.Locations } => modules.Contains("LOCATIONS"),
                    KnowledgeRead { Topic: KnowledgeTopic.BusinessHours } => modules.Contains("BUSINESS_HOURS"),
                    KnowledgeRead { Topic: KnowledgeTopic.Faqs } => modules.Contains("FAQ"),
                    AvailabilityRead => modules.Contains("RESERVATIONS") && modules.Contains("SERVICES"), _ => false
                };
            }
            if (!await Allowed().WaitAsync(deadline.Token)) return Empty(BusinessReadStatus.Denied);
            BusinessReadResult result;
            if (request is KnowledgeRead read)
            {
                var value = await knowledge.QueryAsync(turn.WorkspaceId, new() { WorkspaceId = turn.WorkspaceId },
                    new() { Topic = read.Topic, LocationId = read.LocationId, SearchTerm = read.SearchTerm, Offset = read.Offset, Limit = read.Limit }, deadline.Token).WaitAsync(deadline.Token);
                if (value.Status == KnowledgeStatus.Denied) return Empty(BusinessReadStatus.Denied);
                if (value.Source != read.Topic || read.LocationId != null && value.LocationId != read.LocationId || value.Facts.Length > 6000)
                    return Empty(BusinessReadStatus.Unavailable);
                var facts = value.Offerings.SelectMany(o => new[] {
                    new VerifiedFact("Offering", o.Name, VerifiedFactSource.BusinessRepository, o.Id),
                    new VerifiedFact("PriceMinorUnits", o.PriceMinorUnits.ToString(System.Globalization.CultureInfo.InvariantCulture), VerifiedFactSource.BusinessRepository, o.Id),
                    new VerifiedFact("Currency", o.Currency, VerifiedFactSource.BusinessRepository, o.Id)
                }).ToArray();
                result = new(turn.WorkspaceId, value.Status == KnowledgeStatus.Found ? BusinessReadStatus.Found
                    : value.Status == KnowledgeStatus.Unavailable ? BusinessReadStatus.Unavailable : BusinessReadStatus.NotFound,
                    source, value.LocationId, facts.Length > 0 ? facts : value.Found ? [new(source, value.Facts, VerifiedFactSource.BusinessRepository)] : [], value);
            }
            else
            {
                if (reservations == null) return Empty(BusinessReadStatus.Unavailable);
                var availability = (AvailabilityRead)request;
                var slots = await (availability.LocalTime is { } time
                    ? reservations.GetAvailabilityAtAsync(turn.WorkspaceId, availability.LocationId, availability.ServiceId, availability.LocalDate.Date.Add(time.ToTimeSpan()), deadline.Token)
                    : reservations.GetAvailabilityAsync(turn.WorkspaceId, availability.LocationId, availability.ServiceId, availability.LocalDate.Date, deadline.Token)).WaitAsync(deadline.Token);
                var page = slots.Where(s => s.IsAvailable).OrderBy(s => s.StartTime).Take(5).ToArray();
                result = new(turn.WorkspaceId, page.Length == 0 ? BusinessReadStatus.NotFound : BusinessReadStatus.Found, source, scope,
                    page.Select(s => new VerifiedFact("AvailableStartUtc", s.StartTime.ToString("O"), VerifiedFactSource.TransactionEngine, availability.ServiceId)).ToArray(), Slots: Array.AsReadOnly(page));
            }
            return await Allowed().WaitAsync(deadline.Token) ? result : Empty(BusinessReadStatus.Denied);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception) { return Empty(BusinessReadStatus.Unavailable); }
    }
}
