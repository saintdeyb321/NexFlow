using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Domain.Enums;
using CacheContextDto = NexFlow.Application.Abstractions.Cache.ConversationContextDto;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IAiResponseOrchestrator
{
    Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken);
}

public sealed class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly IAiInterpreter _interpreter;
    private readonly IEntitlementService _entitlementService;
    private readonly IBookingFlow _bookingFlow;
    private readonly IRequestFlow _requestFlow;
    private readonly ISupportFlow _supportFlow;
    private readonly IChatFlow _chatFlow;
    private readonly IConversationRepository _conversationRepo;
    private readonly IConversationCache _cache;
    private readonly IMessageGateway _messageGateway;
    private readonly ILogger<AiResponseOrchestrator> _logger;

    public AiResponseOrchestrator(
        IAiInterpreter interpreter, IEntitlementService entitlementService,
        IBookingFlow bookingFlow, IRequestFlow requestFlow, ISupportFlow supportFlow, IChatFlow chatFlow,
        IConversationRepository conversationRepo, IConversationCache cache, IMessageGateway messageGateway,
        ILogger<AiResponseOrchestrator> logger)
    {
        _interpreter = interpreter; _entitlementService = entitlementService;
        _bookingFlow = bookingFlow; _requestFlow = requestFlow; _supportFlow = supportFlow; _chatFlow = chatFlow;
        _conversationRepo = conversationRepo; _cache = cache; _messageGateway = messageGateway;
        _logger = logger;
    }

    public async Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        var context = await _cache.GetContextAsync(workspaceId, normalizedPhone, cancellationToken) ?? new CacheContextDto();
        var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var interpretation = await _interpreter.InterpretAsync(workspaceId, request.MessageText, context.CurrentGoal ?? "", activeModules, cancellationToken);

        string finalResponse;

        if ((interpretation.Intent == "BOOKING" || context.CurrentGoal == "BOOKING") && activeModules.Contains("RESERVATIONS"))
        {
            finalResponse = await _bookingFlow.ProcessAsync(workspaceId, normalizedPhone, conversation.Id, context, interpretation, request.CustomerName, cancellationToken);
        }
        else if (interpretation.Intent == "REQUEST" && activeModules.Contains("REQUESTS"))
        {
            finalResponse = await _requestFlow.ProcessAsync(workspaceId, normalizedPhone, request.MessageText, conversation.Id, cancellationToken);
        }
        else if (interpretation.Intent == "SUPPORT")
        {
            finalResponse = await _supportFlow.ProcessAsync(workspaceId, conversation.Id, cancellationToken);
        }
        else
        {
            // 🔥 RAG FIX: Ahora le pasamos el workspaceId al ChatFlow para que pueda consultar la base de datos
            finalResponse = await _chatFlow.ProcessAsync(workspaceId, request.MessageText, cancellationToken);
        }

        await _cache.SetContextAsync(workspaceId, normalizedPhone, context, cancellationToken);
        await PersistAndSendAsync(workspaceId, normalizedPhone, conversation.Id, finalResponse, cancellationToken);
    }

    private async Task PersistAndSendAsync(Guid workspaceId, string phone, string conversationId, string text, CancellationToken ct)
    {
        var pendingId = Guid.NewGuid().ToString();
        await _conversationRepo.AddMessageAsync(workspaceId, conversationId, new MessageRecord { Id = pendingId, ExternalMessageId = pendingId, Direction = "outbound", Sender = SenderType.AI, Content = text, Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow }, ct);
        try
        {
            var extId = await _messageGateway.SendTextAsync(workspaceId, phone, text, pendingId, ct);
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Sent, extId, ct);
        }
        catch
        {
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Failed, null, ct);
        }
    }
}