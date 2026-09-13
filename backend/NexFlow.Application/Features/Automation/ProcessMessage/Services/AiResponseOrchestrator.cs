using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Dispatcher;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;
using System.Text.Json;
using System.Text.RegularExpressions;

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
    private readonly IContextResolver _contextResolver;
    private readonly ILogger<AiResponseOrchestrator> _logger;

    public AiResponseOrchestrator(
        IIntentEngine intentEngine, IModuleDispatcher moduleDispatcher, IAiRouter aiRouter,
        IConversationRepository conversationRepo, IConversationCache conversationCache,
        IMessageGateway messageGateway, IContextResolver contextResolver, ILogger<AiResponseOrchestrator> logger)
    {
        _intentEngine = intentEngine; _moduleDispatcher = moduleDispatcher; _aiRouter = aiRouter;
        _conversationRepo = conversationRepo; _conversationCache = conversationCache;
        _messageGateway = messageGateway; _contextResolver = contextResolver; _logger = logger;
    }

    public async Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        if (conversation.Mode != ConversationMode.Automatic) return;

        // 🔥 CORRECCIÓN: Ruta absoluta para evitar la ambigüedad del DTO
        var context = await _conversationCache.GetContextAsync(workspaceId, normalizedPhone, cancellationToken) ?? new NexFlow.Application.Abstractions.Cache.ConversationContextDto();

        IntentResultDto? intentResult = null;

        if (!string.IsNullOrEmpty(context.PendingAction))
        {
            intentResult = await TryResolvePendingActionAsync(workspaceId, request.MessageText, context, cancellationToken);
        }

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
                    catch (Exception) // 🔥 CORRECCIÓN: Quitamos el 'ex' para evitar el warning
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
            {
                extId = await _messageGateway.SendDocumentAsync(workspaceId, normalizedPhone, documentUrlToSend, "Documento.pdf", finalResponse, cancellationToken);
            }
            else
            {
                extId = await _messageGateway.SendTextAsync(workspaceId, normalizedPhone, finalResponse, cancellationToken);
            }

            await _conversationCache.MarkMessageAsAiGeneratedAsync(workspaceId, extId, cancellationToken);
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversation.Id, pendingId, MessageStatus.Sent, extId, cancellationToken);
        }
        catch (Exception) // 🔥 CORRECCIÓN: Quitamos el 'ex' para evitar el warning
        {
            _logger.LogError("Fallo al enviar mensaje a través del Gateway.");
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversation.Id, pendingId, MessageStatus.Failed, null, cancellationToken);
            throw;
        }
    }

    // 🔥 CORRECCIÓN: Ruta absoluta en la firma de este método
    private async Task<IntentResultDto?> TryResolvePendingActionAsync(Guid workspaceId, string userMessage, NexFlow.Application.Abstractions.Cache.ConversationContextDto context, CancellationToken ct)
    {
        var msg = userMessage.Trim().ToLowerInvariant();
        if (msg == "cancelar" || msg == "salir" || msg == "detener") return new IntentResultDto(IntentType.CancelReservation, 1.0, new Dictionary<string, string>());
        var currentIntent = Enum.TryParse<IntentType>(context.CurrentIntent, true, out var parsed) ? parsed : IntentType.Unknown;
        if (currentIntent == IntentType.Unknown) return null;

        var parameters = new Dictionary<string, string>();
        switch (context.PendingAction)
        {
            case "ASK_LOCATION":
                var locId = await _contextResolver.GroundLocationAsync(workspaceId, userMessage, ct);
                if (locId != null) parameters["locationId"] = locId;
                break;
            case "ASK_SERVICE":
                var srvId = await _contextResolver.GroundServiceAsync(workspaceId, userMessage, context.SelectedLocationId, ct);
                if (srvId != null) parameters["serviceId"] = srvId;
                break;
            case "ASK_DATE":
                var date = await _contextResolver.GroundDateAsync(workspaceId, userMessage, ct);
                if (date != null) parameters["date"] = date;
                break;
            case "ASK_TIME":
                var time = ExtractTimeFast(userMessage);
                if (time != null) parameters["time"] = time;
                break;
            case "ASK_NAME":
                if (userMessage.Length > 2) parameters["name"] = userMessage.Trim();
                break;
        }
        if (parameters.Any()) return new IntentResultDto(currentIntent, 1.0, parameters);
        return null;
    }

    private string? ExtractTimeFast(string text)
    {
        var match = Regex.Match(text.ToLower(), @"(\d{1,2})(?::(\d{2}))?\s*(am|pm|de la mañana|de la tarde|de la noche)?");
        if (match.Success)
        {
            int hour = int.Parse(match.Groups[1].Value);
            int min = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
            string period = match.Groups[3].Value;
            if (period.Contains("pm") || period.Contains("tarde") || period.Contains("noche")) { if (hour < 12) hour += 12; }
            else if (period.Contains("am") || period.Contains("mañana")) { if (hour == 12) hour = 0; }
            return $"{hour:D2}:{min:D2}";
        }
        return null;
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
                    {
                        sb.AppendLine($"• *{day.GetProperty("day").GetString()}:* {day.GetProperty("schedule").GetString()}");
                    }
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