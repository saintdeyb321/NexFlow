using System.Security.Cryptography;
using System.Text;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IOutboundMessageService
{
    Task<bool> TryResumeResponseAsync(Guid workspaceId, string sourceInboundMessageId, CancellationToken ct);
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

    private static string GetKey(Guid workspaceId, SenderType sender, string source) => $"{workspaceId:N}:{sender}:{source}:response:1";
    private static string GetId(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public async Task<bool> TryResumeResponseAsync(Guid workspaceId, string sourceInboundMessageId, CancellationToken ct)
    {
        var existing = await _conversationRepo.GetOutboundAsync(workspaceId,
            GetId(GetKey(workspaceId, SenderType.AI, sourceInboundMessageId)), ct);
        if (!existing.HasValue) return false;
        if (existing.Value.Message.Status == MessageStatus.Sent) return true;
        // Resume the persisted response without re-running business workflows.
        await SendMessageAsync(workspaceId, existing.Value.ConversationId, existing.Value.Phone,
            existing.Value.Message.Content, SenderType.AI, sourceInboundMessageId, ct);
        return true;
    }
    public async Task<MessageRecord> SendMessageAsync(Guid workspaceId, string conversationId, string phone, string content, SenderType sender, string sourceInboundMessageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Message content is required.", nameof(content));
        if (sender is not (SenderType.AI or SenderType.BusinessUser)) throw new ArgumentException("Invalid outbound sender.", nameof(sender));
        var source = string.IsNullOrWhiteSpace(sourceInboundMessageId) ? Guid.NewGuid().ToString("N") : sourceInboundMessageId;
        var key = GetKey(workspaceId, sender, source);
        var id = GetId(key);
        var prepared = await _conversationRepo.PrepareOutboundAsync(workspaceId, conversationId, phone, new MessageRecord
        {
            Id = id,
            IdempotencyKey = key,
            Origin = sender == SenderType.AI ? MessageOrigin.NexFlowAI : MessageOrigin.NexFlowHuman,
            Direction = "outbound",
            Sender = sender,
            Content = content,
            Status = MessageStatus.Pending,
            Timestamp = DateTime.UtcNow
        }, ct);

        if (!prepared.SendRequired)
        {
            if (prepared.Message.Status == MessageStatus.Sent) return prepared.Message;
            throw new InvalidOperationException($"Outbound {id} has an unconfirmed transport attempt; reconciliation is required before retrying.");
        }

        string externalId;
        try
        {
            externalId = await _messageGateway.SendTextAsync(workspaceId, phone, prepared.Message.Content, key, ct);
            if (string.IsNullOrWhiteSpace(externalId))
                throw new InvalidOperationException("Evolution did not return a provider message ID.");
        }
        catch (Exception ex)
        {
            var rejected = ex is HttpRequestException { StatusCode: not null } http &&
                (int)http.StatusCode.Value >= 400 && (int)http.StatusCode.Value < 500 &&
                http.StatusCode != System.Net.HttpStatusCode.RequestTimeout;
            // Timeouts, cancellation and malformed confirmations leave the attempt
            // pending. Retrying transport without provider evidence could duplicate it.
            using var failureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await _conversationRepo.RecordOutboundFailureAsync(workspaceId, id, ex.Message, rejected, failureTimeout.Token);
            }
            catch (Exception persistenceError)
            {
                throw new AggregateException("Transport failed and its failure could not be persisted.", ex, persistenceError);
            }
            throw;
        }
        // Preserve the provider confirmation even if the caller disconnected.
        using var confirmationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await _conversationRepo.ConfirmOutboundAsync(workspaceId, id, externalId, confirmationTimeout.Token);
        return prepared.Message with { ExternalMessageId = externalId, Status = MessageStatus.Sent };
    }
}
