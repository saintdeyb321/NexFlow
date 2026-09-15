using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Dispatcher;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;
using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;

// 🔥 CORRECCIÓN DE AMBIGÜEDAD: Creamos un alias explícito para el DTO correcto.
using CacheContextDto = NexFlow.Application.Abstractions.Cache.ConversationContextDto;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IAiResponseOrchestrator
{
    Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken);
}

public class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly IIntentEngine _intentEngine;
    private readonly IModuleDispatcher _moduleDispatcher;
    private readonly IAiRouter _aiRouter;
    private readonly IConversationRepository _conversationRepo;
    private readonly IConversationCache _conversationCache;
    private readonly IMessageGateway _messageGateway;
    private readonly IReservationParameterResolver _parameterResolver; // Inyectamos el nuevo especialista
    private readonly ILogger<AiResponseOrchestrator> _logger;

    public AiResponseOrchestrator(
        IIntentEngine intentEngine, IModuleDispatcher moduleDispatcher, IAiRouter aiRouter,
        IConversationRepository conversationRepo, IConversationCache conversationCache,
        IMessageGateway messageGateway, IReservationParameterResolver parameterResolver, ILogger<AiResponseOrchestrator> logger)
    {
        _intentEngine = intentEngine; _moduleDispatcher = moduleDispatcher; _aiRouter = aiRouter;
        _conversationRepo = conversationRepo; _conversationCache = conversationCache;
        _messageGateway = messageGateway; _parameterResolver = parameterResolver; _logger = logger;
    }

    public async Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        if (conversation.Mode != ConversationMode.Automatic) return;

        // Utilizamos el alias para evitar la ambigüedad
        CacheContextDto context = await _conversationCache.GetContextAsync(workspaceId, normalizedPhone, cancellationToken) ?? new CacheContextDto();

        IntentResultDto? intentResult = null;

        // 1. Fase Resolutiva: Si hay una acción pendiente, el especialista intenta extraer el dato.
        if (!string.IsNullOrEmpty(context.PendingAction))
        {
            intentResult = await _parameterResolver.ResolveAsync(workspaceId, request.MessageText, context, cancellationToken);
        }

        // 2. Fase Analítica: Si no estábamos a la mitad de algo, IA analiza la intención.
        if (intentResult == null)
        {
            intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, context, cancellationToken);
        }

        if (!intentResult.IsConfident()) intentResult = new IntentResultDto(IntentType.Unknown, 0, new Dictionary<string, string>());

        string finalResponse;
        string? documentUrlToSend = null;

        if (intentResult.Intent == IntentType.ProviderUnavailable)
            finalResponse = "En este momento estoy actualizando mis sistemas. ¿Podrías intentar consultarme en un par de minutos?";
        else if (intentResult.Intent == IntentType.GeneralGreeting)
            finalResponse = "¡Hola! Soy el asistente virtual. ¿En qué te puedo ayudar el día de hoy?";
        else
        {
            intentResult.Parameters["messageId"] = request.MessageId;
            var systemContext = await _moduleDispatcher.BuildSystemContextAsync(workspaceId, normalizedPhone, intentResult, cancellationToken);

            if (systemContext.RequiresHuman)
            {
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, HandoffReason.AiEscalation, cancellationToken);
            }

            if (systemContext.ModuleCode == "CORE" || !systemContext.Success)
            {
                finalResponse = "No pude procesar esa información en este momento. En breve un asesor se pondrá en contacto contigo para ayudarte.";
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(systemContext.Data))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(systemContext.Data);
                        if (doc.RootElement.TryGetProperty("pdfUrl", out var pdfUrlProp) && pdfUrlProp.ValueKind == JsonValueKind.String)
                        {
                            documentUrlToSend = pdfUrlProp.GetString();
                        }
                    }
                    catch { /* Ignoramos si no es JSON válido */ }
                }

                string? deterministicResponse = TryFormatDeterministicResponse(systemContext.ModuleCode, systemContext.Data);

                if (!string.IsNullOrEmpty(deterministicResponse))
                {
                    finalResponse = deterministicResponse;
                }
                else
                {
                    try
                    {
                        finalResponse = await _aiRouter.GenerateResponseAsync(workspaceId, systemContext, context, cancellationToken);
                    }
                    catch (Exception)
                    {
                        _logger.LogError("Fallo crítico en GeminiAiProvider durante orquestación.");
                        finalResponse = "Tuve una pequeña demora procesando tu solicitud. ¿Podrías repetirla por favor?";
                    }
                }
            }
        }

        var pendingId = Guid.NewGuid().ToString();
        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord
        {
            Id = pendingId,
            ExternalMessageId = pendingId,
            Direction = "outbound",
            Sender = SenderType.AI,
            Content = finalResponse,
            Status = MessageStatus.Pending,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        try
        {
            string extId;
            if (!string.IsNullOrEmpty(documentUrlToSend))
                extId = await _messageGateway.SendDocumentAsync(workspaceId, normalizedPhone, documentUrlToSend, "Documento.pdf", finalResponse, cancellationToken);
            else
                extId = await _messageGateway.SendTextAsync(workspaceId, normalizedPhone, finalResponse, cancellationToken);

            await _conversationCache.MarkMessageAsAiGeneratedAsync(workspaceId, extId, cancellationToken);
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversation.Id, pendingId, MessageStatus.Sent, extId, cancellationToken);
        }
        catch (Exception)
        {
            _logger.LogError("Fallo al enviar mensaje a través del Gateway.");
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversation.Id, pendingId, MessageStatus.Failed, null, cancellationToken);
            throw;
        }
    }

    private string? TryFormatDeterministicResponse(string moduleCode, string? jsonData)
    {
        if (string.IsNullOrWhiteSpace(jsonData) || jsonData.Trim() == "{}") return null;

        try
        {
            using var doc = JsonDocument.Parse(jsonData);
            var root = doc.RootElement;

            if ((moduleCode == "CATALOG" || moduleCode == "SERVICES") && root.TryGetProperty("status", out var catStatus))
            {
                if (catStatus.GetString() == "too_many_results") return "¡Tenemos una gran variedad de opciones! 📄 Te adjunto nuestro catálogo completo en PDF para que lo revises con mayor comodidad. Si buscas algo en específico, dime qué necesitas.";
                if (catStatus.GetString() == "empty") return "Lo siento, actualmente no tenemos ítems disponibles en nuestro catálogo.";
            }

            if (root.TryGetProperty("status", out var statusProp) && statusProp.GetString() == "missing_parameter")
            {
                var paramName = root.TryGetProperty("parameter", out var pProp) ? pProp.GetString() : "";
                if (paramName == "location") return "¡Claro! Para continuar, ¿me podrías indicar en cuál de nuestras sedes deseas hacer la reserva?";
                if (paramName == "service") return "¿Qué servicio específico te gustaría reservar?";
                if (paramName == "date") return "¿Para qué fecha deseas agendar tu turno? (Ej. Mañana, el viernes, 15 de octubre)";
                if (paramName == "time") return "¿A qué hora te gustaría asistir?";
                if (paramName == "name") return "Para dejar la reserva a tu nombre, ¿me podrías indicar tu nombre completo?";
            }

            if (moduleCode == "BUSINESS_HOURS" && root.TryGetProperty("status", out var hoursStatus) && hoursStatus.GetString() == "SUCCESS")
            {
                if (root.TryGetProperty("data", out var daysArray))
                {
                    var sb = new System.Text.StringBuilder("🕒 *Nuestros horarios de atención son:*\n\n");
                    foreach (var day in daysArray.EnumerateArray())
                        sb.AppendLine($"• *{day.GetProperty("day").GetString()}:* {day.GetProperty("schedule").GetString()}");
                    return sb.ToString();
                }
            }

            if (moduleCode == "REQUESTS" && root.TryGetProperty("status", out var reqStatus))
            {
                if (reqStatus.GetString() == "FOUND") return $"📄 *Estado de tu trámite*\nTu solicitud de '{root.GetProperty("title").GetString()}' se encuentra en estado: *{root.GetProperty("requestStatus").GetString()}*.";
                if (reqStatus.GetString() == "CREATED") return "✅ Hemos guardado tu solicitud exitosamente. Un asesor revisará tu caso a la brevedad posible.";
            }

            if (moduleCode == "LOCATIONS" && root.TryGetProperty("status", out var locStatus) && locStatus.GetString() == "success")
            {
                if (root.TryGetProperty("locations", out var locArray))
                {
                    var sb = new System.Text.StringBuilder("📍 *Nuestras Sedes:*\n\n");
                    foreach (var loc in locArray.EnumerateArray())
                    {
                        sb.AppendLine($"• *{loc.GetProperty("name").GetString()}:* {loc.GetProperty("address").GetString()}");
                        if (loc.TryGetProperty("mapsUrl", out var mUrl) && !string.IsNullOrWhiteSpace(mUrl.GetString())) sb.AppendLine($"  🗺️ Ver mapa: {mUrl.GetString()}");
                    }
                    return sb.ToString();
                }
            }
        }
        catch { return null; }
        return null;
    }
}