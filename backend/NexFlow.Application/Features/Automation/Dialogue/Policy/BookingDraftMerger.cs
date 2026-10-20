using System.Globalization;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Features.Automation.Dialogue.Policy;

internal static class BookingDraftMerger
{
    internal static async Task ApplyAsync(Guid workspace, string source, ConversationContextDto state,
        BookingDraftProposal proposal, IOfferingService offerings, CancellationToken ct)
    {
        if (proposal.WorkspaceId != workspace || proposal.SourceMessageId != source || proposal.Understanding.Context.WorkspaceId != workspace
            || proposal.Understanding.Context.SourceMessageId != source)
            throw new InvalidOperationException("Booking proposal identity mismatch.");
        if (state.StateVersion != proposal.ExpectedStateVersion)
            throw new ConcurrencyException("Conversation changed before applying the booking proposal.");
        var value = proposal.Understanding;
        state.CurrentGoal = "RESERVATION";
        if (value.Location.TryGetValue(out var location) && state.SelectedLocationId != location.Id)
        {
            if (state.SelectedLocationId != null) state.PendingServiceIds.Clear();
            state.SelectedLocationId = location.Id; state.SelectedServiceId = null;
            state.TargetTime = null; state.PreferredTime = null; state.ReservationServiceOffset = 0;
            state.PendingDialogueSlots.Remove("Time"); state.PendingDialogueSlots.Remove("Service");
        }
        if (value.Date.TryGetValue(out var date) && state.TargetDate != date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
        {
            state.TargetDate = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            state.TargetTime = null; state.PreferredTime = null;
            state.PendingDialogueSlots.Remove("Time");
        }
        if (value.Name.TryGetValue(out var name)) { state.RealCustomerName = name; state.PendingCustomerName = null; }
        else if (value.Name.Code == "SurnameMissing") state.PendingCustomerName = value.Name.Candidates[0].Value;
        if (value.Service.TryGetValue(out var service)) state.PendingServiceIds = [service.Id];
        else if (value.Service.Status == ResolutionStatus.Ambiguous) state.PendingServiceIds = value.Service.Candidates.Select(c => c.Value.Id).ToList();
        await ResolvePendingServiceAsync(workspace, state, offerings, ct);
        if (value.Time.TryGetValue(out var time))
        {
            state.PreferredTime = time.Kind == TimePreferenceKind.Exact ? null : time;
            state.TargetTime = time.Kind == TimePreferenceKind.Exact ? time.ExactTime!.Value.ToString("HH:mm", CultureInfo.InvariantCulture) : null;
        }
        Resolved("Name", value.Name.Status); Resolved("Location", value.Location.Status);
        if (state.SelectedServiceId != null) Resolved("Service", value.Service.Status);
        Resolved("Date", value.Date.Status); Resolved("Time", value.Time.Status);
        foreach (var slot in proposal.UnresolvedSlots)
            if (!state.PendingDialogueSlots.Contains(slot)) state.PendingDialogueSlots.Add(slot);
        state.RecoveryOptions.Clear(); state.RecoveryOptionsContext = null;
        void Resolved(string slot, ResolutionStatus status)
        { if (status == ResolutionStatus.Determined) state.PendingDialogueSlots.Remove(slot); }
    }

    internal static async Task ResolvePendingServiceAsync(Guid workspace, ConversationContextDto state, IOfferingService offerings, CancellationToken ct)
    {
        if (state.SelectedLocationId == null || state.PendingServiceIds.Count != 1) return;
        var candidate = await offerings.GetServiceByIdAsync(workspace, state.PendingServiceIds[0], ct);
        if (candidate is { IsActive: true, RequiresReservation: true, DurationInMinutes: >= 5 }
            && await offerings.IsServiceAvailableAtLocationAsync(workspace, candidate.Id, state.SelectedLocationId, ct))
        {
            if (state.SelectedServiceId != candidate.Id) { state.TargetTime = null; state.PreferredTime = null; state.PendingDialogueSlots.Remove("Time"); }
            state.SelectedServiceId = candidate.Id; state.PendingServiceIds.Clear(); state.PendingDialogueSlots.Remove("Service");
        }
        else
        {
            state.SelectedServiceId = null; state.PendingServiceIds.Clear();
            if (!state.PendingDialogueSlots.Contains("Service")) state.PendingDialogueSlots.Add("Service");
        }
    }
}
