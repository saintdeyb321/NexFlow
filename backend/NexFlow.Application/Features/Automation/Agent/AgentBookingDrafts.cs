using System.Globalization;
using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Agent;

// Applies verified, reversible preparation to a detached context. The journal owns
// its eventual CAS. There is no reservation/order writer or outbound dependency.
public sealed class AgentBookingDrafts(IWorkspaceSlotResolver slots, IOfferingService offerings,
    IEntitlementService entitlements, IBusinessReadTools reads, IClock clock) : IAgentBookingDrafts
{
    public async Task<AgentDraftStage> StageAsync(AgentTurnRequest request, ConversationContextDto origin, AgentBookingPatch patch,
        int remainingTools, CancellationToken ct)
    {
        var modules = (await entitlements.GetAvailableModuleCodesAsync(request.WorkspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (origin.Mode == "Human" || origin.Focus != null || origin.CurrentGoal is not (null or "BOOKING" or "RESERVATION")
            || !modules.Contains("SERVICES") || !modules.Contains("RESERVATIONS")) throw new InvalidOperationException("Preparation denied.");
        var supplied = new[] { ("Location", patch.Location), ("Service", patch.Service), ("Date", patch.Date), ("Time", patch.Time), ("Name", patch.Name) };
        if (supplied.All(s => s.Item2 == null)) return new(Clone(origin));
        if (supplied.Any(p => p.Item2 is { } c && (string.IsNullOrWhiteSpace(c.Value) || c.Value.Length > 160
            || string.IsNullOrWhiteSpace(c.Evidence) || c.Evidence.Length > 300 || !AiEvidenceValidator.HasFragment(request.Text, c.Evidence))))
            return Clarify("los datos", [], origin);
        var readPlan = new SlotReadPlan(patch.Location != null || patch.Name != null || origin.SelectedLocationId != null,
            patch.Service != null || patch.Name != null || origin.SelectedServiceId != null, patch.Name != null);
        var resolved = await slots.ResolveAsync(request.WorkspaceId, request.SourceMessageId, request.Text, origin,
            SlotResolutionPurpose.Reservation, readPlan, ct);
        // Only slots explicitly proposed by the semantic owner may change. Local
        // parsing supplies validation/catalogs, never a competing intent decision.
        var empty = resolved with { Name = SlotResolution<string>.Missing(), Location = SlotResolution<ResolvedBusinessEntity>.Missing(),
            Service = SlotResolution<ResolvedBusinessEntity>.Missing(), Product = SlotResolution<ResolvedBusinessEntity>.Missing(),
            Date = SlotResolution<DateOnly>.Missing(), Time = SlotResolution<TimePreference>.Missing() };
        var ai = new AiInterpretation { Intent = ConversationIntent.Reservation, Act = DialogueAct.Correction,
            Location = patch.Location?.Value, Service = patch.Service?.Value, Date = patch.Date?.Value,
            Time = patch.Time?.Value == "Period" ? null : patch.Time?.Value, CustomerName = patch.Name?.Value,
            Evidence = supplied.Where(p => p.Item2 != null).Select(p => new AiSlotEvidence(p.Item1, p.Item2!.Evidence, p.Item2.Value)).ToList() };
        var valid = AiEvidenceValidator.Validate(empty, ai, request.Text);
        valid = valid with {
            Location = Reference(patch.Location, "AGENT_LOCATION", valid.Location, resolved.AuthorizedLocations),
            Service = Reference(patch.Service, "AGENT_SERVICE", valid.Service, resolved.AuthorizedServices)
        };
        if (patch.Time?.Value == "Period") {
            var period = new TimePreferenceResolver().ResolveEvidence(patch.Time.Evidence);
            valid = valid with { Time = period.TryGetValue(out var preference) && preference.Kind != TimePreferenceKind.Exact
                ? period : SlotResolution<TimePreference>.Invalid("UnsubstantiatedPeriod") };
        }
        if (patch.Location != null && valid.Location.Status != ResolutionStatus.Determined) return Clarify("la sede", resolved.AuthorizedLocations, origin, "AGENT_LOCATION");
        if (patch.Service != null && valid.Service.Status != ResolutionStatus.Determined) return Clarify("el servicio", resolved.AuthorizedServices, origin, "AGENT_SERVICE");
        if (patch.Date != null && (valid.Date.Status != ResolutionStatus.Determined || valid.Date.Candidates[0].Value < DateOnly.FromDateTime(resolved.Context.LocalBusinessNow.DateTime)))
            return Clarify("la fecha", [], origin);
        if (patch.Time != null && valid.Time.Status != ResolutionStatus.Determined) return Clarify("la hora o franja que prefieres", [], origin);
        if (patch.Name != null && valid.Name.Status != ResolutionStatus.Determined) return Clarify("tu nombre y apellido", [], origin);
        var next = Clone(origin);
        await BookingDraftMerger.ApplyAsync(request.WorkspaceId, request.SourceMessageId, next,
            new(request.WorkspaceId, request.SourceMessageId, origin.StateVersion, valid, null, []), offerings, ct);
        // A changed site/service may invalidate only its dependent service/time.
        if (next.SelectedServiceId != null && next.SelectedLocationId != null) {
            var service = await offerings.GetServiceByIdAsync(request.WorkspaceId, next.SelectedServiceId, ct);
            if (service is not { IsActive: true, RequiresReservation: true, DurationInMinutes: >= 5 }
                || !await offerings.IsServiceAvailableAtLocationAsync(request.WorkspaceId, next.SelectedServiceId, next.SelectedLocationId, ct)) {
                next.SelectedServiceId = null; next.PendingServiceIds.Clear();
            }
        }
        var exact = next.TargetTime ?? (next.PreferredTime is { Kind: TimePreferenceKind.Exact, ExactTime: { } preferred } ? preferred.ToString("HH:mm", CultureInfo.InvariantCulture) : null);
        string? availability = null; var toolCalls = 0;
        if (exact != null) {
            var time = TimeOnly.ParseExact(exact, "HH:mm", CultureInfo.InvariantCulture);
            next.TargetTime = null; next.PreferredTime = new(TimePreferenceKind.Exact, ExactTime: time);
            if (remainingTools > 0 && next.SelectedLocationId != null && next.SelectedServiceId != null
                && DateOnly.TryParseExact(next.TargetDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) {
                var check = await reads.ReadAsync(reads.Open(request.WorkspaceId), new AvailabilityRead(next.SelectedLocationId, next.SelectedServiceId,
                    date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), time), ct); toolCalls = 1;
                var zone = TimeZoneInfo.FindSystemTimeZoneById(resolved.Context.TimeZoneId);
                if (check.WorkspaceId == request.WorkspaceId && check.Source == "Availability" && check.LocationId == next.SelectedLocationId
                    && check.Status == BusinessReadStatus.Found && check.Slots?.Any(s => s.IsAvailable && s.StartTime.Kind == DateTimeKind.Utc
                        && TimeZoneInfo.ConvertTimeFromUtc(s.StartTime, zone) == date.ToDateTime(time)) == true) {
                    next.TargetTime = exact; next.PreferredTime = null;
                    availability = "Ese horario aparece disponible en esta consulta; todavía no hay una cita registrada.";
                } else availability = check.Status == BusinessReadStatus.NotFound
                    ? "No encontré una opción disponible a esa hora en esta consulta. Conservé tu preferencia para buscar otra alternativa."
                    : "Conservé tu hora preferida, pero ahora no pude comprobar su disponibilidad.";
            }
        }
        next.ReservationCommitSourceMessageId = null;
        if (next.PendingRecoveryAction is "AGENT_CANCEL_SCOPE" or "AGENT_CANCEL_CONFIRM") next.PendingRecoveryAction = null;
        next.CurrentStep = MissingStep(next);
        var latest = (await entitlements.GetAvailableModuleCodesAsync(request.WorkspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!modules.SetEquals(latest) || !await entitlements.IsLicenseValidAsync(request.WorkspaceId, ct))
            throw new InvalidOperationException("Preparation capability changed.");
        return new(next, AvailabilityExplanation: availability, ToolCalls: toolCalls);

        SlotResolution<ResolvedBusinessEntity> Reference(AgentSlotCandidate? candidate, string kind, SlotResolution<ResolvedBusinessEntity> literal,
            IReadOnlyList<BusinessEntityOption> authorized)
        {
            if (candidate == null) return literal;
            var question = ConversationQuestion.Current(request.WorkspaceId, origin, clock.UtcNow);
            if (candidate.ReferenceIndex == null) {
                if (literal.Status == ResolutionStatus.Determined || candidate.QuestionId == null) return literal;
                // Natural references are preparation only, backed by an actual
                // V2 publication and a unique currently authorized entity of this
                // field. The original user evidence is preserved, never fabricated.
                if (question?.ConversationId != request.ConversationId || question.QuestionId != candidate.QuestionId
                    || question.Expected is not (ExpectedReply.Information or ExpectedReply.RequestedData)
                    || origin.AgentV2?.QuestionSourceMessageId != question.SourceMessageId)
                    return SlotResolution<ResolvedBusinessEntity>.Invalid("StaleConversationReference");
                var referenceKind = kind == "AGENT_SERVICE" ? "Service" : "Location";
                var ids = (origin.AgentV2.EntityReferences ?? []).Where(r => r.Kind == referenceKind).Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
                var referenced = authorized.Where(e => ids.Contains(e.Id)).DistinctBy(e => e.Id).ToArray();
                if (referenced.Length != 1 || referenced[0].Id != candidate.Value) return SlotResolution<ResolvedBusinessEntity>.Invalid("AmbiguousConversationReference");
                var entity = referenced[0];
                return SlotResolution<ResolvedBusinessEntity>.Match(new(entity.Id, entity.Name, entity.LocationId), candidate.Evidence, entity.Id, EvidenceSource.ValidatedLlm);
            }
            var options = origin.RecoveryOptions;
            if (question is not { Expected: ExpectedReply.Selection } || question.ConversationId != request.ConversationId
                || question.QuestionId != candidate.QuestionId || origin.RecoveryOptionsContext != "AGENT_OPTIONS"
                || candidate.ReferenceIndex is < 1 or > 5 || candidate.ReferenceIndex > options.Count) return SlotResolution<ResolvedBusinessEntity>.Invalid("StaleReference");
            var indexed = options[candidate.ReferenceIndex.Value - 1];
            if (indexed.Kind != kind || options.Count > 1 && OrderMentionResolver.Ordinal(OrderMentionResolver.Words(candidate.Evidence)) != candidate.ReferenceIndex - 1
                || !authorized.Any(e => e.Id == indexed.Key) || candidate.Value != indexed.Key) return SlotResolution<ResolvedBusinessEntity>.Invalid("UnsubstantiatedReference");
            var option = authorized.Single(e => e.Id == indexed.Key);
            return SlotResolution<ResolvedBusinessEntity>.Match(new(option.Id, option.Name, option.LocationId), candidate.Evidence, option.Id, EvidenceSource.CurrentMenu);
        }
    }
    public static string MissingStep(ConversationContextDto c) => c.SelectedServiceId == null && c.PendingServiceIds.Count != 1 ? "COLLECT_SERVICE"
        : c.SelectedLocationId == null ? "COLLECT_LOCATION" : c.TargetDate == null ? "COLLECT_DATE"
        : c.TargetTime == null ? "COLLECT_TIME" : c.RealCustomerName == null ? "COLLECT_CUSTOMERNAME" : "AGENT_PREVIEW";
    private static ConversationContextDto Clone(ConversationContextDto c) => JsonSerializer.Deserialize<ConversationContextDto>(JsonSerializer.Serialize(c))!;
    private static AgentDraftStage Clarify(string field, IReadOnlyList<BusinessEntityOption> choices, ConversationContextDto origin, string kind = "")
        => new(Clone(origin), "Quisiera precisar " + field + ".", choices.Take(5).Select(c => new ConversationMenuOption(c.Id, c.Name, kind)).ToArray());
}
