using System.Diagnostics;
using Microsoft.Extensions.Options;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.Agent;

public sealed class AgentDialogueRuntime(LlmFirstShadowAgent planner, IAgentTurnJournal journal,
    IContextRecoveryService contexts, IConversationRepository conversations, IEntitlementService entitlements,
    IClock clock, IOptions<AgentDialogueOptions> options) : IExperimentalAgentRuntime
{
    public bool Handles(Guid workspace) => options.Value.Enabled && workspace != Guid.Empty
        && workspace == options.Value.SyntheticWorkspaceId && AgentExperimentScope.Current?.Workspace == workspace;

    public async Task<AgentTurnResult> HandleAsync(AgentTurnRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var watch = Stopwatch.StartNew(); var calls = 0; var toolCalls = 0;
        if (!Handles(request.WorkspaceId)) return AgentTurnResult.Rejected("DialogueDisabled");
        try
        {
            var origin = await contexts.GetOrRecoverContextAsync(request.WorkspaceId, request.ClientKey, ct);
            if (!await Authorized(request, origin.Mode == "Human", ct)) return AgentTurnResult.Rejected("DialogueDenied");
            if (origin.Focus != null) return AgentTurnResult.Rejected("UnsupportedContext", elapsed: watch.ElapsedMilliseconds)
                with { StatusExplanation = "Esta preparación necesita revisión antes de usar la conversación experimental." };
            var begin = await journal.BeginAsync(request, origin, options.Value, ct);
            if (begin.Lease == null) return await Recover(request, begin, ct);
            var proposed = await planner.EvaluateOwnedAsync(request, begin.Lease, ct);
            calls = proposed.ModelCalls; toolCalls = proposed.ToolCalls;
            var modules = (await entitlements.GetAvailableModuleCodesAsync(request.WorkspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!await Authorized(request, false, ct)) return AgentTurnResult.Rejected("DialogueDenied");
            var realized = AgentResponseAuthority.Prepare(request, origin, proposed, modules, clock.UtcNow);
            var prepared = await journal.CompleteAsync(request, begin.Lease, realized.Next, realized.Result, ct);
            var result = await Recover(request, prepared, ct);
            ConversationDiagnostics.Count("AgentDialogueResult", result.Outcome);
            return result with { ModelCalls = proposed.ModelCalls, ToolCalls = proposed.ToolCalls, ElapsedMilliseconds = watch.ElapsedMilliseconds, Recovered = false };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch
        {
            // A lost ACK may hide an already committed result. Observe never creates
            // a new claim and never repeats the model or a business effect.
            try { return (await Recover(request, await journal.ObserveAsync(request, ct), ct)) with { ModelCalls = calls, ToolCalls = toolCalls, ElapsedMilliseconds = watch.ElapsedMilliseconds }; }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { return AgentTurnResult.Rejected("JournalUnavailable", elapsed: watch.ElapsedMilliseconds)
                with { StatusExplanation = "No pude recuperar la respuesta con seguridad. Necesitamos comprobar el estado de la preparación antes de continuar; puedes consultar de nuevo más tarde." }; }
        }
    }
    private async Task<bool> Authorized(AgentTurnRequest request, bool human, CancellationToken ct)
    {
        if (!Handles(request.WorkspaceId) || human) return false;
        var conversation = await conversations.GetConversationAsync(request.WorkspaceId, request.ConversationId, ct);
        return conversation is { Mode: ConversationMode.Automatic, Status: "open" } && conversation.Id == request.ConversationId
            && conversation.ConsumerPhone == request.ClientKey
            && !await conversations.HasUnconfirmedOutboundAsync(request.WorkspaceId, request.ClientKey, ct)
            && await entitlements.IsLicenseValidAsync(request.WorkspaceId, ct);
    }
    private async Task<AgentTurnResult> Recover(AgentTurnRequest request, AgentJournalEntry entry, CancellationToken ct)
    {
        var current = await contexts.GetOrRecoverContextAsync(request.WorkspaceId, request.ClientKey, ct);
        var ownHandoff = entry.Result?.Proposal?.Decision == AgentDecision.HumanAssistance && current.StateVersion == entry.AppliedVersion;
        if (!await Authorized(request, current.Mode == "Human" && !ownHandoff, ct)) return AgentTurnResult.Rejected("DialogueDenied");
        if (entry.Status == AgentJournalStatus.Conflict) return AgentTurnResult.Rejected("EvaluationIdentityConflict")
            with { StatusExplanation = "No puedo reutilizar esta consulta con datos distintos." };
        if (entry.Status == AgentJournalStatus.Started) return AgentTurnResult.Rejected("InProgress") with { StatusExplanation = AgentSafeReplies.Pending };
        if (entry.Result == null) return AgentTurnResult.Rejected("ResultNotFound") with { StatusExplanation = AgentSafeReplies.Interrupted };
        if (entry.AppliedVersion != current.StateVersion) return AgentTurnResult.Rejected("StaleResult") with { StatusExplanation = AgentSafeReplies.Stale };
        var modules = (await entitlements.GetAvailableModuleCodesAsync(request.WorkspaceId, ct)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // Revalidate the capabilities used to construct a cached public response.
        var needed = entry.Result.Proposal?.Decision is AgentDecision.PrepareReservation or AgentDecision.ProposeDiscardDraft or AgentDecision.ClarifyCancellationScope
            ? new[] { "SERVICES", "RESERVATIONS" } : Array.Empty<string>();
        if (needed.Any(m => !modules.Contains(m)) || (entry.Result.RequiredModules ?? []).Any(m => !modules.Contains(m))) return AgentTurnResult.Rejected("CapabilityDenied")
                with { StatusExplanation = "Esa información ya no está habilitada para este negocio." };
        return entry.Result with { ModelCalls = 0, ToolCalls = 0, Recovered = true };
    }
}
