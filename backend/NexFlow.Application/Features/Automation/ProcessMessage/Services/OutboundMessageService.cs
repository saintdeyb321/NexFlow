using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IOutboundMessageService
{
    // 🔥 SPRINT 02: Añadimos sourceInboundMessageId a la firma
    Task<MessageRecord> SendMessageAsync(Guid workspaceId, string conversationId, string phone, string content, SenderType sender, string sourceInboundMessageId, CancellationToken ct);
}

public class OutboundMessageService : IOutboundMessageService
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IMessageGateway _messageGateway;

    public OutboundMessageService(IConversationRepository conversationRepo, IMessageGateway messageGateway)
    {
        _conversationRepo = conversationRepo;
        _messageGateway = messageGateway;
    }

    public async Task<MessageRecord> SendMessageAsync(Guid workspaceId, string conversationId, string phone, string content, SenderType sender, string sourceInboundMessageId, CancellationToken ct)
    {
        var pendingId = Guid.NewGuid().ToString();
        var initialRecord = new MessageRecord
        {
            Id = pendingId,
            ExternalMessageId = pendingId,
            Direction = "outbound",
            Sender = sender,
            Content = content,
            Status = MessageStatus.Pending,
            Timestamp = DateTime.UtcNow
        };

        await _conversationRepo.AddMessageAsync(workspaceId, conversationId, initialRecord, ct);

        string externalId;
        try
        {
            // 🔥 SPRINT 02: Generamos una clave determinista única basada en el mensaje que originó esta respuesta
            var idempotencyKey = string.IsNullOrWhiteSpace(sourceInboundMessageId)
                ? pendingId
                : $"{sourceInboundMessageId}:response:1";

            externalId = await _messageGateway.SendTextAsync(workspaceId, phone, content, idempotencyKey, ct);

            if (string.IsNullOrWhiteSpace(externalId))
                throw new InvalidOperationException("Evolution API no confirmó el envío del mensaje.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Failed, null, ct);
            return initialRecord with { Status = MessageStatus.Failed };
        }

        await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Sent, externalId, ct);
        return initialRecord with { ExternalMessageId = externalId, Status = MessageStatus.Sent };
    }
}