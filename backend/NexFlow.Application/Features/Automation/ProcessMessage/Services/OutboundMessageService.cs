using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IOutboundMessageService
{
    Task<MessageRecord> SendMessageAsync(Guid workspaceId, string conversationId, string phone, string content, SenderType sender, CancellationToken ct);
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

    public async Task<MessageRecord> SendMessageAsync(Guid workspaceId, string conversationId, string phone, string content, SenderType sender, CancellationToken ct)
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

        // 1. Guardamos como PENDING primero
        await _conversationRepo.AddMessageAsync(workspaceId, conversationId, initialRecord, ct);

        try
        {
            // 2. Intentamos enviar a Evolution / WhatsApp
            var externalId = await _messageGateway.SendTextAsync(workspaceId, phone, content, pendingId, ct);

            // 3. Actualizamos a SENT
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Sent, externalId, ct);

            return initialRecord with { ExternalMessageId = externalId, Status = MessageStatus.Sent };
        }
        catch (Exception)
        {
            // 3. Fallo: Actualizamos a FAILED
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Failed, null, ct);

            return initialRecord with { Status = MessageStatus.Failed };
        }
    }
}