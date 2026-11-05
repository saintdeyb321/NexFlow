using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Features.Automation.AgentV2;

// Selects an engine, never interprets the consumer's language. Reuses the existing
// durable conversation, journal, handoff and outbound services, without legacy UX.
public sealed class AgentV2Ingress(IOptionsMonitor<AgentV2Options> options, IAgentDemoPolicy protection,
    IAgentV2Runtime runtime, IAgentTurnJournal journal, IConversationRepository conversations,
    IContextRecoveryService contexts, IEntitlementService entitlements, IOutboundMessageService outbound,
    IHumanHandoffService handoff, ILogger<AgentV2Ingress> logger) : IAgentV2Ingress
{
    public async Task<bool> TryHandleAsync(Guid workspace, string phone, ProcessIncomingMessageCommand input, CancellationToken ct)
    {
        var configured = options.CurrentValue.Workspaces.TryGetValue(workspace, out var selection);
        // Ordinary legacy tenants incur no additional reads. The durable DEMO
        // protection still blocks commercial writers if V2 configuration disappears.
        if (!configured && !protection.Protects(workspace)) return false;
        var current = await contexts.GetOrRecoverContextAsync(workspace, phone, ct);
        if (!configured && current.AgentV2 == null) return false;
        if (selection is not { Enabled: true, Revision: > 0 }
            || !protection.Protects(workspace) || !protection.Allows(workspace, input.InstanceName, phone)
            || current.AgentV2 != null && current.AgentV2.Revision != selection.Revision) {
            logger.LogWarning("ConversationEngine=AgentV2 Outcome=RouteBlocked Revision={Revision}", selection?.Revision);
            return true; // A selected/previously used V2 turn must NEVER fall through to Legacy.
        }
        logger.LogInformation("ConversationEngine=AgentV2 Outcome=Ingress Revision={Revision}", selection.Revision);
        var channel = new AgentDemoChannel(protection, runtime, journal, conversations, contexts, entitlements, outbound, handoff);
        if (await channel.ResumeAsync(workspace, phone, input, ct) is { } recovered) {
            EnsureFinal(recovered); return true;
        }
        var conversation = await conversations.GetOrCreateActiveConversationAsync(workspace, phone, ct);
        await conversations.AddMessageAsync(workspace, conversation.Id, new MessageRecord {
            Id = input.MessageId, ExternalMessageId = input.MessageId, Origin = MessageOrigin.Consumer,
            Direction = "inbound", Sender = SenderType.Consumer, Content = input.MessageText[..Math.Min(8000, input.MessageText.Length)],
            Kind = MessageKind.Text,
            Timestamp = input.ObservedAtUtc ?? DateTime.UtcNow, Status = MessageStatus.Sent
        }, ct);
        // Human ownership and unsupported media never enter the model. No media understanding is claimed.
        if (conversation.Mode != ConversationMode.Automatic || current.Mode == "Human" || input.Kind != IncomingMessageKind.Text) return true;
        EnsureFinal(await channel.RespondAsync(workspace, phone, input, conversation, ct));
        return true;
    }
    private static void EnsureFinal(MessageRecord message)
    {
        if (message.Status is not (MessageStatus.Sent or MessageStatus.Failed or MessageStatus.UnknownDelivery))
            throw new ConcurrencyException("AgentV2 outbound remains pending in the existing durable lifecycle.");
    }
}
