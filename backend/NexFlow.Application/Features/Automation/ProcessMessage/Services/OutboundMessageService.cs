using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IOutboundMessageService
{
    Task<bool> TryResumeResponseAsync(Guid workspaceId, string sourceInboundMessageId, CancellationToken ct);
    Task<MessageRecord> SendMessageAsync(Guid workspaceId, string conversationId, string phone, string content, SenderType sender, string sourceInboundMessageId, CancellationToken ct);
    Task<MessageRecord> SendDocumentAsync(Guid workspaceId, string conversationId, string phone, string caption, string mediaUrl, string fileName, string scope, string sourceInboundMessageId, CancellationToken ct);
}

public class OutboundMessageService : IOutboundMessageService
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IMessageGateway _messageGateway;
    private readonly INotificationService _notifications;
    private readonly ILogger<OutboundMessageService> _logger;

    public OutboundMessageService(IConversationRepository conversationRepo, IMessageGateway messageGateway,
        INotificationService notifications, ILogger<OutboundMessageService> logger)
    {
        _conversationRepo = conversationRepo;
        _messageGateway = messageGateway;
        _notifications = notifications;
        _logger = logger;
    }

    private static string GetKey(Guid workspaceId, SenderType sender, string source) => $"{workspaceId:N}:{sender}:{source}:response:1";
    private static string GetDocumentKey(Guid workspaceId, string source, string scope) => $"{workspaceId:N}:{SenderType.AI}:{source}:response:document:{scope}:1";
    private static string GetId(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));

    public async Task<bool> TryResumeResponseAsync(Guid workspaceId, string sourceInboundMessageId, CancellationToken ct)
    {
        var keys = new[] { GetKey(workspaceId, SenderType.AI, sourceInboundMessageId),
            GetDocumentKey(workspaceId, sourceInboundMessageId, "PRODUCT"),
            GetDocumentKey(workspaceId, sourceInboundMessageId, "SERVICE") };
        foreach (var key in keys)
        {
            var existing = await _conversationRepo.GetOutboundAsync(workspaceId, GetId(key), ct);
            if (!existing.HasValue) continue;
            if (existing.Value.Message.Status is MessageStatus.Sent or MessageStatus.Failed) return true;
            if (existing.Value.Message.Status is MessageStatus.UnknownDelivery or MessageStatus.Attempting)
            {
                await FinalizeUnconfirmedAsync(workspaceId, existing.Value.Message);
                return true;
            }
            // Resume the persisted response, including media, without querying
            // artifacts or executing the business workflow again.
            await SendPreparedAsync(workspaceId, existing.Value.Phone, existing.Value.Message, ct);
            return true;
        }
        return false;
    }

    public async Task<MessageRecord> SendMessageAsync(Guid workspaceId, string conversationId, string phone, string content,
        SenderType sender, string sourceInboundMessageId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new ArgumentException("Message content is required.", nameof(content));
        if (sender is not (SenderType.AI or SenderType.BusinessUser)) throw new ArgumentException("Invalid outbound sender.", nameof(sender));
        var source = string.IsNullOrWhiteSpace(sourceInboundMessageId) ? Guid.NewGuid().ToString("N") : sourceInboundMessageId;
        var key = GetKey(workspaceId, sender, source);
        var id = GetId(key);
        var prepared = await _conversationRepo.PrepareOutboundAsync(workspaceId, conversationId, phone, new MessageRecord
        {
            Id = id, IdempotencyKey = key,
            Origin = sender == SenderType.AI ? MessageOrigin.NexFlowAI : MessageOrigin.NexFlowHuman,
            Direction = "outbound", Sender = sender, Content = content,
            Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow
        }, ct);
        return await SendPreparedAsync(workspaceId, phone, prepared.Message, ct);
    }

    public async Task<MessageRecord> SendDocumentAsync(Guid workspaceId, string conversationId, string phone,
        string caption, string mediaUrl, string fileName, string scope, string sourceInboundMessageId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceInboundMessageId);
        ArgumentException.ThrowIfNullOrWhiteSpace(caption);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (scope is not ("PRODUCT" or "SERVICE")) throw new ArgumentException("Invalid catalog scope.", nameof(scope));
        if (!Uri.TryCreate(mediaUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ArgumentException("A valid document URL is required.", nameof(mediaUrl));
        var key = GetDocumentKey(workspaceId, sourceInboundMessageId, scope);
        var prepared = await _conversationRepo.PrepareOutboundAsync(workspaceId, conversationId, phone, new MessageRecord
        {
            Id = GetId(key), IdempotencyKey = key, Origin = MessageOrigin.NexFlowAI,
            Direction = "outbound", Sender = SenderType.AI, Content = caption,
            Kind = MessageKind.Document, MediaUrl = mediaUrl, FileName = fileName,
            Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow
        }, ct);
        return await SendPreparedAsync(workspaceId, phone, prepared.Message, ct);
    }

    private async Task<MessageRecord> SendPreparedAsync(Guid workspaceId, string phone, MessageRecord message, CancellationToken ct)
    {
        var id = message.Id;
        var key = message.IdempotencyKey ?? throw new InvalidOperationException("Outbound idempotency key is required.");
        if (message.Status != MessageStatus.Pending)
        {
            if (message.Status is MessageStatus.Sent or MessageStatus.Failed or MessageStatus.Attempting) return message;
            return await FinalizeUnconfirmedAsync(workspaceId, message);
        }

        var transportStarted = false;
        string? externalId = null;
        try
        {
            async Task TransportStarting(CancellationToken token)
            {
                if (!await _conversationRepo.StartOutboundAsync(workspaceId, id, token))
                    throw new TransportAlreadyClaimedException();
                transportStarted = true;
            }
            externalId = message.Kind switch
            {
                MessageKind.Text => await _messageGateway.SendTextAsync(workspaceId, phone, message.Content, key, TransportStarting, ct),
                MessageKind.Document => await _messageGateway.SendDocumentAsync(workspaceId, phone,
                    message.MediaUrl ?? throw new InvalidOperationException("Missing persisted media URL."),
                    message.FileName ?? throw new InvalidOperationException("Missing persisted file name."),
                    message.Content, key, TransportStarting, ct),
                _ => throw new InvalidOperationException("Unsupported outbound kind.")
            };
            if (string.IsNullOrWhiteSpace(externalId))
                throw new InvalidOperationException("Evolution did not return a provider message ID.");
            using var confirmationTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await _conversationRepo.ConfirmOutboundAsync(workspaceId, id, externalId, confirmationTimeout.Token);
            return await GetPersistedAsync(workspaceId, id);
        }
        catch (TransportAlreadyClaimedException)
        {
            var existing = await GetPersistedAsync(workspaceId, id);
            if (existing.Status is MessageStatus.Sent or MessageStatus.Failed or MessageStatus.Attempting) return existing;
            return await FinalizeUnconfirmedAsync(workspaceId, existing);
        }
        catch (Exception ex)
        {
            var rejected = transportStarted && externalId == null && ex is HttpRequestException { StatusCode: not null } http &&
                (int)http.StatusCode.Value >= 400 && (int)http.StatusCode.Value < 500 &&
                http.StatusCode != System.Net.HttpStatusCode.RequestTimeout;
            var status = rejected ? MessageStatus.Failed : transportStarted ? MessageStatus.UnknownDelivery : MessageStatus.Pending;
            using var failureTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try
            {
                await _conversationRepo.RecordOutboundFailureAsync(workspaceId, id, ex.Message, status, failureTimeout.Token, externalId);
            }
            catch (Exception persistenceError)
            {
                throw new AggregateException("Transport failed and its outcome could not be persisted.", ex, persistenceError);
            }
            var persisted = await GetPersistedAsync(workspaceId, id);
            if (persisted.Status == MessageStatus.Sent) return persisted; // Echo won the confirmation race.
            if (persisted.Status is MessageStatus.UnknownDelivery or MessageStatus.Failed)
            {
                await ReportDeliveryProblemAsync(workspaceId, persisted, ex);
                return persisted; // Durable outcome; safely finish this inbound.
            }
            _logger.LogError(ex, "Outbound {MessageId} in workspace {WorkspaceId} failed before POST; prepared response remains retryable.", id, workspaceId);
            throw;
        }
    }

    private async Task<MessageRecord> GetPersistedAsync(Guid workspaceId, string id)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var existing = await _conversationRepo.GetOutboundAsync(workspaceId, id, timeout.Token);
        return existing?.Message ?? throw new KeyNotFoundException("Persisted outbound not found.");
    }

    private async Task<MessageRecord> FinalizeUnconfirmedAsync(Guid workspaceId, MessageRecord message)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        if (message.ProviderConfirmationId != null)
        {
            try
            {
                // Real provider evidence may repair a failed local confirmation;
                // this never invokes the transport a second time.
                await _conversationRepo.ConfirmOutboundAsync(workspaceId, message.Id, message.ProviderConfirmationId, timeout.Token);
                return await GetPersistedAsync(workspaceId, message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Provider confirmation for outbound {MessageId} could not be persisted.", message.Id);
            }
        }
        if (message.Status == MessageStatus.Attempting)
            await _conversationRepo.RecordOutboundFailureAsync(workspaceId, message.Id,
                "An earlier transport attempt has no durable confirmation; automatic resend is disabled.", MessageStatus.UnknownDelivery, timeout.Token);
        var persisted = await GetPersistedAsync(workspaceId, message.Id);
        if (persisted.Status == MessageStatus.UnknownDelivery)
            await ReportDeliveryProblemAsync(workspaceId, persisted);
        return persisted;
    }

    private async Task ReportDeliveryProblemAsync(Guid workspaceId, MessageRecord message, Exception? error = null)
    {
        _logger.LogError(error, "Outbound {MessageId} in workspace {WorkspaceId}: {Status}. Provider evidence={ProviderId}. Automatic resend disabled. Reason={Reason}",
            message.Id, workspaceId, message.Status, message.ProviderConfirmationId, message.LastError);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            await _notifications.NotifyAsync(workspaceId, "CONVERSATIONS", NotificationType.SystemAlert,
                message.Status == MessageStatus.UnknownDelivery ? "Entrega de mensaje sin confirmar" : "Mensaje rechazado",
                $"Envío {message.Id}: {message.Status}. No se reenviará automáticamente; revisa el chat y el proveedor.",
                "/conversations", timeout.Token);
        }
        catch (Exception notificationError)
        {
            _logger.LogError(notificationError, "Could not notify workspace {WorkspaceId} about outbound {MessageId}; its durable outcome is preserved.", workspaceId, message.Id);
        }
    }

    private sealed class TransportAlreadyClaimedException : Exception { }
}
