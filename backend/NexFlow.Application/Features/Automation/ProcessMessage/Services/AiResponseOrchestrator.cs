using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

// 🔥 CORRECCIÓN: Alias explícito
using CacheContextDto = NexFlow.Application.Abstractions.Cache.ConversationContextDto;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

// 🔥 CORRECCIÓN: Interfaz restaurada
public interface IAiResponseOrchestrator
{
    Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken);
}

public sealed class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly IAiProvider _aiProvider;
    private readonly ICapabilityExecutor _capabilityExecutor;
    private readonly IConversationRepository _conversationRepo;
    private readonly IConversationCache _cache;
    private readonly IMessageGateway _messageGateway;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<AiResponseOrchestrator> _logger;

    public AiResponseOrchestrator(
        IAiProvider aiProvider, ICapabilityExecutor capabilityExecutor,
        IConversationRepository conversationRepo, IConversationCache cache,
        IMessageGateway messageGateway, IEntitlementService entitlementService,
        ILogger<AiResponseOrchestrator> logger)
    {
        _aiProvider = aiProvider; _capabilityExecutor = capabilityExecutor;
        _conversationRepo = conversationRepo; _cache = cache;
        _messageGateway = messageGateway; _entitlementService = entitlementService;
        _logger = logger;
    }

    public async Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        if (conversation.Mode != ConversationMode.Automatic) return;

        try
        {
            var context = await _cache.GetContextAsync(workspaceId, normalizedPhone, cancellationToken) ?? new CacheContextDto();
            var rawHistory = await _conversationRepo.GetMessagesAsync(workspaceId, conversation.Id, 10, cancellationToken);

            var aiHistory = rawHistory.OrderBy(m => m.Timestamp).Select(m => new AiMessage(m.Sender == SenderType.AI ? "assistant" : "user", m.Content)).ToList();
            if (!aiHistory.Any()) aiHistory.Add(new AiMessage("user", request.MessageText));

            var activeModules = (await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var (systemPrompt, tools) = await _capabilityExecutor.GetContextAndToolsAsync(workspaceId, activeModules, context.SelectedLocationId, cancellationToken);

            var aiResponse = await _aiProvider.GenerateChatResponseAsync(systemPrompt, aiHistory, tools, cancellationToken);
            string finalResponseText;

            if (aiResponse.ToolCall != null)
            {
                var toolCall = aiResponse.ToolCall;
                string toolResult = await _capabilityExecutor.ExecuteToolAsync(toolCall, activeModules, workspaceId, normalizedPhone, conversation.Id, context, cancellationToken);
                await _cache.SetContextAsync(workspaceId, normalizedPhone, context, cancellationToken);

                // Obligatorio para Gemini: Inyectar resultado como "user" para evitar Error 400
                string safeToolName = toolCall.Name ?? "unknown";
                aiHistory.Add(new AiMessage("user", $"[SISTEMA - RESULTADO DE '{safeToolName}']:\n{toolResult}\nResponde basándote en esto de forma natural."));

                var finalAiCall = await _aiProvider.GenerateChatResponseAsync(systemPrompt, aiHistory, null, cancellationToken);
                finalResponseText = finalAiCall.Text ?? "Tuvimos un inconveniente al procesar tu solicitud.";
            }
            else
            {
                finalResponseText = aiResponse.Text ?? "Disculpa, no logré comprender tu mensaje.";
            }

            await PersistAndSendAsync(workspaceId, normalizedPhone, conversation.Id, finalResponseText, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.Message.Contains("503") || ex.Message.Contains("500") || ex.Message.Contains("429"))
        {
            _logger.LogError(ex, "Saturación de API de Google (503/429). Activando respuesta de emergencia.");
            await PersistAndSendAsync(workspaceId, normalizedPhone, conversation.Id, "Mis circuitos están saturados por alta demanda en este instante 😅. Por favor, inténtalo de nuevo en unos minutos.", cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error crítico en orquestación para {Phone}.", normalizedPhone);
        }
    }

    private async Task PersistAndSendAsync(Guid workspaceId, string phone, string conversationId, string text, CancellationToken ct)
    {
        var pendingId = Guid.NewGuid().ToString();
        await _conversationRepo.AddMessageAsync(workspaceId, conversationId, new MessageRecord { Id = pendingId, ExternalMessageId = pendingId, Direction = "outbound", Sender = SenderType.AI, Content = text, Status = MessageStatus.Pending, Timestamp = DateTime.UtcNow }, ct);
        try
        {
            var extId = await _messageGateway.SendTextAsync(workspaceId, phone, text, ct);
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Sent, extId, ct);
        }
        catch
        {
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversationId, pendingId, MessageStatus.Failed, null, ct);
        }
    }
}