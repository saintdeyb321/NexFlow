using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Features.Automation.Agent;

public interface IAgentDemoChannel
{
    bool Protects(Guid workspace);
    bool Allows(Guid workspace, string instance, string phone);
    Task<MessageRecord?> ResumeAsync(Guid workspace, string phone, ProcessIncomingMessageCommand request, CancellationToken ct);
    Task<MessageRecord> RespondAsync(Guid workspace, string phone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken ct);
}

// One journal for preparation, one existing outbound lifecycle for delivery.
// No gateway, reservation/order writer, notification claim or second outbox.
public sealed class AgentDemoChannel(IAgentDemoPolicy policy, IExperimentalAgentRuntime runtime, IAgentTurnJournal journal,
    IConversationRepository conversations, IContextRecoveryService contexts, IEntitlementService entitlements,
    IOutboundMessageService outbound, IHumanHandoffService handoff) : IAgentDemoChannel
{
    public bool Protects(Guid workspace) => policy.Protects(workspace);
    public bool Allows(Guid workspace, string instance, string phone) => policy.Allows(workspace, instance, phone);
    public async Task<MessageRecord?> ResumeAsync(Guid workspace, string phone, ProcessIncomingMessageCommand input, CancellationToken ct)
    {
        if (!Allows(workspace, input.InstanceName, phone)) return null;
        var conversation = await conversations.GetActiveConversationAsync(workspace, phone, ct);
        if (conversation == null || conversation.Status != "open") return null;
        var request = Request(workspace, phone, input, conversation);
        var entry = await journal.ObserveAsync(request, ct);
        if (entry.Result == null || entry.Status is AgentJournalStatus.Started or AgentJournalStatus.Conflict) return null;
        return await Deliver(workspace, phone, input, conversation, request, entry, ct);
    }
    public async Task<MessageRecord> RespondAsync(Guid workspace, string phone, ProcessIncomingMessageCommand input, ConversationRecord conversation, CancellationToken ct)
    {
        if (!Allows(workspace, input.InstanceName, phone)) throw new ConcurrencyException("DEMO route is not authorized.");
        var request = Request(workspace, phone, input, conversation);
        using var scope = new AgentDemoScope(workspace, phone, input.InstanceName);
        var evaluated = await runtime.HandleAsync(request, ct);
        if (!evaluated.Publishable) throw new ConcurrencyException("DEMO result is not durably prepared or no longer authorized.");
        return await Deliver(workspace, phone, input, conversation, request, await journal.ObserveAsync(request, ct), ct);
    }
    private async Task<MessageRecord> Deliver(Guid workspace, string phone, ProcessIncomingMessageCommand input, ConversationRecord conversation,
        AgentTurnRequest request, AgentJournalEntry entry, CancellationToken ct)
    {
        if (!Allows(workspace, input.InstanceName, phone) || !await entitlements.IsLicenseValidAsync(workspace, ct))
            throw new ConcurrencyException("DEMO authorization changed.");
        var result = entry.Result ?? throw new ConcurrencyException("DEMO receipt unavailable.");
        var existing = await conversations.GetOutboundAsync(workspace, OutboundMessageService.ResponseId(workspace, SenderType.AI, input.MessageId), ct);
        if (existing is { } envelope) {
            if (envelope.ConversationId != conversation.Id || envelope.Phone != phone) throw new ConcurrencyException("DEMO outbound identity mismatch.");
            // These statuses never cause another POST. Preserve UnknownDelivery.
            if (envelope.Message.Status is MessageStatus.Sent or MessageStatus.Failed or MessageStatus.UnknownDelivery or MessageStatus.Attempting) {
                await outbound.TryResumeResponseAsync(workspace, input.MessageId, ct);
                return (await conversations.GetOutboundAsync(workspace, envelope.Message.Id, ct))!.Value.Message;
            }
        }
        var current = await contexts.GetOrRecoverContextAsync(workspace, phone, ct);
        var ownHumanRequest = result.Handoff == AgentHandoffState.Requested && current.CurrentQuestion?.SourceMessageId == input.MessageId
            && current.HandoffReason is "AgentHandoffRequested" or "AiEscalation";
        if (!ownHumanRequest) {
            using var scope = new AgentDemoScope(workspace, phone, input.InstanceName);
            // Read/validate the prepared result, never synthesize a replacement.
            var recovered = await runtime.HandleAsync(request, ct);
            if (!recovered.Publishable || recovered.VerifiedText != result.VerifiedText) throw new ConcurrencyException("DEMO prepared result is stale.");
        }
        if (!result.Publishable || existing is { } pending && pending.Message.Content != result.VerifiedText)
            throw new ConcurrencyException("DEMO response does not match its journal.");
        if (ownHumanRequest) {
            var latest = await conversations.GetConversationAsync(workspace, conversation.Id, ct);
            if (latest == null || latest.Mode == ConversationMode.Human && latest.HandoffReason != HandoffReason.AiEscalation)
                throw new ConcurrencyException("Manual takeover superseded the DEMO handoff request.");
            if (latest.Mode != ConversationMode.Human) await handoff.EscalateToHumanAsync(workspace, conversation.Id, HandoffReason.AiEscalation, ct);
            // Durable mode persisted; business notification and human acceptance
            // are not claimed. The exact journaled request text is used.
            return await outbound.SendHandoffAcknowledgementAsync(workspace, conversation.Id, phone, result.VerifiedText!, input.MessageId, ct);
        }
        return await outbound.SendMessageAsync(workspace, conversation.Id, phone, result.VerifiedText!, SenderType.AI, input.MessageId, ct);
    }
    private static AgentTurnRequest Request(Guid workspace, string phone, ProcessIncomingMessageCommand input, ConversationRecord c)
        => new(workspace, c.Id, phone, input.MessageId, input.MessageText);
}
