using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Shared.DTOs;

namespace NexFlow.Application.Features.Automation.Dialogue.Understanding;

public interface IWorkspaceSlotResolver
{
    Task<CommerceCatalog> ReadCommerceAsync(Guid workspaceId, ConversationContextDto context, bool products, bool services, bool order, string? locationId, CancellationToken ct);
    Task<IReadOnlyList<BusinessEntityOption>> ReadCommerceLocationsAsync(Guid workspaceId, bool order, CancellationToken ct);
    Task<TurnUnderstanding> ResolveAsync(Guid workspaceId, string sourceMessageId, string text,
        ConversationContextDto state, SlotResolutionPurpose purpose, CancellationToken ct);
    Task<TurnUnderstanding> ResolveAsync(Guid workspaceId, string sourceMessageId, string text,
        ConversationContextDto state, SlotResolutionPurpose purpose, SlotReadPlan reads, CancellationToken ct);
}

// Read-only understanding shared by the dialogue policy and AiInterpreter.
// It cannot save context, confirm a draft, query providers, send messages or call a transaction engine.
public sealed class WorkspaceSlotResolver(IEntitlementService entitlements, IBusinessProfileRepository profiles,
    ILocationRepository locations, IOfferingService offerings, IClock clock, NameSlotResolver names,
    LocationMentionResolver locationMentions, OfferingMentionResolver offeringMentions, BusinessDateResolver dates,
    TimePreferenceResolver times, ILogger<WorkspaceSlotResolver> logger) : IWorkspaceSlotResolver
{
    public async Task<IReadOnlyList<BusinessEntityOption>> ReadCommerceLocationsAsync(Guid workspaceId, bool order, CancellationToken ct)
    {
        var modules=(await entitlements.GetAvailableModuleCodesAsync(workspaceId,ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!(order ? modules.Contains("ORDERS") && modules.Contains("CATALOG") : modules.Contains("LOCATIONS"))) return [];
        return (await locations.GetLocationsAsync(workspaceId,ct)).Where(l=>l.Id!=null).Select(l=>new BusinessEntityOption(l.Id!,l.Name)).ToArray();
    }

    public async Task<CommerceCatalog> ReadCommerceAsync(Guid workspaceId, ConversationContextDto context, bool products, bool services, bool order, string? locationId, CancellationToken ct)
    {
        if (workspaceId == Guid.Empty || context.Mode == "Human") throw new InvalidOperationException("Authorized workspace and automatic mode required.");
        var modules=(await entitlements.GetAvailableModuleCodesAsync(workspaceId,ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var productValues=products && modules.Contains("CATALOG") && (!order || modules.Contains("ORDERS"))
            ? (await offerings.GetProductsAsync(workspaceId,locationId,null,ct)).Where(p=>Eligible(p,"PRODUCT",locationId)).Select(VerifiedOffering.Capture).ToArray() : [];
        modules=(await entitlements.GetAvailableModuleCodesAsync(workspaceId,ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var serviceValues=services && modules.Contains("SERVICES")
            ? (await offerings.GetServicesAsync(workspaceId,locationId,null,ct)).Where(s=>Eligible(s,"SERVICE",locationId)).Select(VerifiedOffering.Capture).ToArray() : [];
        return new(workspaceId,context.StateVersion,Array.AsReadOnly(productValues),Array.AsReadOnly(serviceValues),Array.AsReadOnly(Array.Empty<BusinessEntityOption>()));
    }

    public Task<TurnUnderstanding> ResolveAsync(Guid workspaceId, string sourceMessageId, string text,
        ConversationContextDto state, SlotResolutionPurpose purpose, CancellationToken ct)
        => ResolveAsync(workspaceId, sourceMessageId, text, state, purpose, SlotReadPlan.All, ct);

    public async Task<TurnUnderstanding> ResolveAsync(Guid workspaceId, string sourceMessageId, string text,
        ConversationContextDto state, SlotResolutionPurpose purpose, SlotReadPlan reads, CancellationToken ct)
    {
        if (workspaceId == Guid.Empty) throw new ArgumentException("Workspace required.", nameof(workspaceId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceMessageId);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(state);
        if (text.Length > 2000) throw new ArgumentException("Turn exceeds the assistant input limit.", nameof(text));
        if (!Enum.IsDefined(purpose)) throw new ArgumentOutOfRangeException(nameof(purpose));
        if (state.Mode == "Human") throw new InvalidOperationException("Automatic understanding is disabled under human control.");
        ct.ThrowIfCancellationRequested();
        var modules = (await entitlements.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profile = await profiles.GetProfileAsync(workspaceId, ct);
        // Same default as AiInterpreter. Invalid configured zones surface; never invent business time.
        var zone = TimeZoneInfo.FindSystemTimeZoneById(string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone);
        var utcNow = clock.UtcNow;
        if (utcNow.Kind != DateTimeKind.Utc) throw new InvalidOperationException("IClock must provide UTC.");
        var localNow = TimeZoneInfo.ConvertTime(new DateTimeOffset(utcNow), zone);
        var reservation = purpose == SlotResolutionPurpose.Reservation;
        if (reads.Locations) modules = (await entitlements.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canReserve = modules.Contains("RESERVATIONS") && modules.Contains("SERVICES");
        var canReadLocations = reservation ? canReserve : modules.Contains("LOCATIONS");
        var authorizedLocations = canReadLocations && reads.Locations ? (await locations.GetLocationsAsync(workspaceId, ct)).ToArray() : [];
        var location = canReadLocations && reads.Locations ? locationMentions.Resolve(text,
            authorizedLocations.Where(l => l.Id != null).Select(l => new BusinessEntityOption(l.Id!, l.Name)).ToArray(), state.CurrentStep == "COLLECT_LOCATION")
            : canReadLocations ? SlotResolution<ResolvedBusinessEntity>.Missing() : SlotResolution<ResolvedBusinessEntity>.Invalid("ModuleDenied");
        // A newly mentioned unique location takes precedence only in this ephemeral resolution scope.
        var scope = location.TryGetValue(out var selected) ? selected.Id
            : location.Status is ResolutionStatus.Ambiguous or ResolutionStatus.Invalid ? null
            : authorizedLocations.Any(l => l.Id == state.SelectedLocationId) ? state.SelectedLocationId : null;
        if (reads.Services) modules = (await entitlements.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canReadServices = modules.Contains("SERVICES") && (!reservation || modules.Contains("RESERVATIONS"));
        BusinessEntityOption[] serviceOptions = [], productOptions = [];
        var service = canReadServices ? SlotResolution<ResolvedBusinessEntity>.Missing() : SlotResolution<ResolvedBusinessEntity>.Invalid("ModuleDenied");
        if (canReadServices && reads.Services)
        {
            var values = (await offerings.GetServicesAsync(workspaceId, scope, null, ct))
                .Where(s => Eligible(s, "SERVICE", scope) && (!reservation || s.RequiresReservation && s.DurationInMinutes >= 5));
            serviceOptions = values.Select(s => new BusinessEntityOption(s.Id, s.Name, VerifiedOffering.Capture(s).Aliases, scope)).ToArray();
            service = offeringMentions.Resolve(text, serviceOptions, state.CurrentStep == "COLLECT_SERVICE");
        }
        if (reads.Products) modules = (await entitlements.GetAvailableModuleCodesAsync(workspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canReadProducts = modules.Contains("CATALOG") && (purpose != SlotResolutionPurpose.Order || modules.Contains("ORDERS"));
        var product = canReadProducts ? SlotResolution<ResolvedBusinessEntity>.Missing() : SlotResolution<ResolvedBusinessEntity>.Invalid("ModuleDenied");
        if (canReadProducts && reads.Products)
        {
            var values = (await offerings.GetProductsAsync(workspaceId, scope, null, ct)).Where(p => Eligible(p, "PRODUCT", scope));
            productOptions = values.Select(p => new BusinessEntityOption(p.Id, p.Name, VerifiedOffering.Capture(p).Aliases, scope)).ToArray();
            product = offeringMentions.Resolve(text, productOptions);
        }
        ct.ThrowIfCancellationRequested();
        var turn = DialogueTurnContext.Capture(workspaceId, sourceMessageId, state, localNow, zone, modules);
        var date = reservation
            ? dates.ResolveReservation(text, localNow, state.CurrentStep == "COLLECT_DATE",
                DateOnly.TryParseExact(state.TargetDate, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var anchorDate) ? anchorDate : null, state.LastQuestion)
            : dates.Resolve(text, localNow, state.CurrentStep == "COLLECT_DATE");
        var result = new TurnUnderstanding(turn, names.Resolve(text, state.CurrentStep == "COLLECT_CUSTOMERNAME"),
            location, service, product, date, times.Resolve(text, state.CurrentStep == "COLLECT_TIME"))
        {
            AuthorizedLocations = authorizedLocations.Where(l => l.Id != null).Select(l => new BusinessEntityOption(l.Id!, l.Name)).ToArray(),
            AuthorizedServices = serviceOptions, AuthorizedProducts = productOptions
        };
        try
        {
            // Fixed, low-cardinality diagnostic fields. No text, name, phone, entity labels or prompt.
            logger.LogInformation("DialogueLocalResolution WorkspaceId={WorkspaceId} NameStatus={NameStatus} LocationStatus={LocationStatus} ServiceStatus={ServiceStatus} ProductStatus={ProductStatus} DateStatus={DateStatus} TimeStatus={TimeStatus}",
                workspaceId, result.Name.Status, result.Location.Status, result.Service.Status, result.Product.Status, result.Date.Status, result.Time.Status);
            foreach (var failure in new[] { DialogueFailure.FromResolution(result.Name), DialogueFailure.FromResolution(result.Location),
                DialogueFailure.FromResolution(result.Service), DialogueFailure.FromResolution(result.Product),
                DialogueFailure.FromResolution(result.Date), DialogueFailure.FromResolution(result.Time) }.OfType<DialogueFailure>())
                logger.LogInformation("DialogueLocalFailure WorkspaceId={WorkspaceId} FailureDomain={FailureDomain} FailureCode={FailureCode}",
                    workspaceId, failure.Domain, failure.Code);
        }
        catch { /* Diagnostics cannot affect understanding. */ }
        return result;
    }

    private static bool Eligible(BusinessOfferingDto offering, string type, string? scope) => offering.IsActive && offering.Type == type
        && (scope == null || string.Equals(offering.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase)
            || string.Equals(offering.LocationScope, "SPECIFIC", StringComparison.OrdinalIgnoreCase) && offering.LocationIds.Contains(scope));
}
