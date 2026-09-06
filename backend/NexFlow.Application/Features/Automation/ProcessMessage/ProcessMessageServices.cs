using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions; // 🔥 Requerido para el extractor rápido de hora
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Dispatcher;
using NexFlow.Application.Engines.Intent;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IIncomingMessageGuard { Task<(bool IsValid, Guid WorkspaceId, string NormalizedPhone)> CheckMessageAsync(ProcessIncomingMessageCommand request, CancellationToken cancellationToken); }

public class IncomingMessageGuard : IIncomingMessageGuard
{
    private readonly IInstanceResolver _instanceResolver;
    private readonly IProcessedMessageRepository _processedMessageRepo;
    private readonly IEntitlementService _entitlementService;
    private readonly ILogger<IncomingMessageGuard> _logger;

    public IncomingMessageGuard(IInstanceResolver instanceResolver, IProcessedMessageRepository processedMessageRepo, IEntitlementService entitlementService, ILogger<IncomingMessageGuard> logger)
    {
        _instanceResolver = instanceResolver; _processedMessageRepo = processedMessageRepo; _entitlementService = entitlementService; _logger = logger;
    }

    private static string NormalizePhone(string phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || phone.Contains("@g.us") || phone.Contains("status@broadcast")) return string.Empty;
        var clean = new string(phone.Split('@')[0].Where(char.IsDigit).ToArray());
        return (clean.Length >= 10 && clean.Length <= 15) ? "+" + clean : string.Empty;
    }

    public async Task<(bool IsValid, Guid WorkspaceId, string NormalizedPhone)> CheckMessageAsync(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var resolvedId = await _instanceResolver.ResolveInstanceAsync(request.InstanceName, cancellationToken);
        if (resolvedId == null || resolvedId == Guid.Empty)
        {
            _logger.LogWarning("Incoming message rejected because the instance '{InstanceName}' could not be resolved.", request.InstanceName);
            return (false, Guid.Empty, string.Empty);
        }

        if (!await _processedMessageRepo.TryAcquireLockAsync(resolvedId.Value, request.MessageId, cancellationToken))
        {
            _logger.LogWarning("Incoming message {MessageId} for workspace {WorkspaceId} was rejected because it is already locked or processed.", request.MessageId, resolvedId.Value);
            return (false, Guid.Empty, string.Empty);
        }

        var normalizedPhone = NormalizePhone(request.CustomerPhone);
        if (string.IsNullOrEmpty(normalizedPhone))
        {
            _logger.LogWarning("Incoming message {MessageId} from instance '{InstanceName}' was rejected because the phone was invalid or empty.", request.MessageId, request.InstanceName);
            return (false, Guid.Empty, string.Empty);
        }

        if (!await _entitlementService.IsLicenseValidAsync(resolvedId.Value, cancellationToken))
        {
            _logger.LogWarning("Incoming message {MessageId} was rejected because the license for workspace {WorkspaceId} is not valid.", request.MessageId, resolvedId.Value);
            return (false, Guid.Empty, string.Empty);
        }

        return (true, resolvedId.Value, normalizedPhone);
    }
}

// --- 2. GESTOR DE ESTADO ---
public interface IConversationStateService { Task<(bool ShouldAiRespond, ConversationRecord Record)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken); }

public class ConversationStateService : IConversationStateService
{
    private readonly IConversationRepository _conversationRepo;
    private readonly IConsumerIdentityRepository _consumerRepo;
    private readonly IConversationCache _conversationCache;
    private readonly ILogger<ConversationStateService> _logger;

    public ConversationStateService(IConversationRepository conversationRepo, IConsumerIdentityRepository consumerRepo, IConversationCache conversationCache, ILogger<ConversationStateService> logger)
    {
        _conversationRepo = conversationRepo; _consumerRepo = consumerRepo; _conversationCache = conversationCache; _logger = logger;
    }

    public async Task<(bool ShouldAiRespond, ConversationRecord Record)> ProcessStateAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepo.GetActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);

        if (request.FromMe)
        {
            if (conversation == null)
            {
                _logger.LogDebug("Ignoring outbound message {MessageId} because there is no active conversation for workspace {WorkspaceId} and phone {NormalizedPhone}.", request.MessageId, workspaceId, normalizedPhone);
                return (false, null!);
            }

            bool isAiMessage = await _conversationCache.IsMessageAiGeneratedAsync(workspaceId, request.MessageId, cancellationToken);
            if (isAiMessage)
            {
                _logger.LogDebug("Ignoring outbound AI-generated message {MessageId} for workspace {WorkspaceId}.", request.MessageId, workspaceId);
                return (false, conversation);
            }

            if (conversation.Mode != ConversationMode.Human)
            {
                _logger.LogInformation("Manual intervention detected for workspace {WorkspaceId} and phone {NormalizedPhone}. Switching conversation {ConversationId} to Human mode.", workspaceId, normalizedPhone, conversation.Id);
                await _conversationRepo.UpdateConversationModeAsync(workspaceId, conversation.Id, ConversationMode.Human, HandoffReason.ManualIntervention, cancellationToken);
                await _conversationCache.DeleteContextAsync(workspaceId, normalizedPhone, cancellationToken);
                conversation = conversation with { Mode = ConversationMode.Human, HandoffReason = HandoffReason.ManualIntervention };
            }

            await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord
            {
                Id = request.MessageId,
                Direction = "outbound",
                Sender = SenderType.BusinessUser,
                Content = request.MessageText,
                ExternalMessageId = request.MessageId,
                Status = MessageStatus.Sent,
                Timestamp = DateTime.UtcNow
            }, cancellationToken);

            return (false, conversation);
        }

        await _consumerRepo.UpsertConsumerAsync(workspaceId, new ConsumerIdentityRecord { Phone = normalizedPhone, DisplayName = request.CustomerName, FirstSeenAt = DateTime.UtcNow, LastInteractionAt = DateTime.UtcNow }, cancellationToken);
        conversation ??= await _conversationRepo.GetOrCreateActiveConversationAsync(workspaceId, normalizedPhone, cancellationToken);

        await _conversationRepo.AddMessageAsync(workspaceId, conversation.Id, new MessageRecord
        {
            Id = request.MessageId,
            Direction = "inbound",
            Sender = SenderType.Consumer,
            Content = request.MessageText,
            ExternalMessageId = request.MessageId,
            Status = MessageStatus.Sent,
            Timestamp = DateTime.UtcNow
        }, cancellationToken);

        bool shouldRespond = conversation.Mode == ConversationMode.Automatic;
        _logger.LogDebug("Inbound message {MessageId} for workspace {WorkspaceId} processed. AI should respond: {ShouldRespond}.", request.MessageId, workspaceId, shouldRespond);
        return (shouldRespond, conversation);
    }
}

// --- 3. ORQUESTADOR IA ---
public interface IAiResponseOrchestrator { Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken); }

public class AiResponseOrchestrator : IAiResponseOrchestrator
{
    private readonly IIntentEngine _intentEngine;
    private readonly IModuleDispatcher _moduleDispatcher;
    private readonly IAiRouter _aiRouter;
    private readonly IConversationRepository _conversationRepo;
    private readonly IConversationCache _conversationCache;
    private readonly IMessageGateway _messageGateway;
    private readonly IContextResolver _contextResolver; // 🔥 SPRINT 6: Inyectado
    private readonly ILogger<AiResponseOrchestrator> _logger;

    public AiResponseOrchestrator(
        IIntentEngine intentEngine,
        IModuleDispatcher moduleDispatcher,
        IAiRouter aiRouter,
        IConversationRepository conversationRepo,
        IConversationCache conversationCache,
        IMessageGateway messageGateway,
        IContextResolver contextResolver,
        ILogger<AiResponseOrchestrator> logger)
    {
        _intentEngine = intentEngine; _moduleDispatcher = moduleDispatcher; _aiRouter = aiRouter;
        _conversationRepo = conversationRepo; _conversationCache = conversationCache;
        _messageGateway = messageGateway; _contextResolver = contextResolver; _logger = logger;
    }

    public async Task RespondAsync(Guid workspaceId, string normalizedPhone, ProcessIncomingMessageCommand request, ConversationRecord conversation, CancellationToken cancellationToken)
    {
        if (conversation.Mode != ConversationMode.Automatic)
        {
            return;
        }

        var context = await _conversationCache.GetContextAsync(workspaceId, normalizedPhone, cancellationToken) ?? new NexFlow.Application.Abstractions.Cache.ConversationContextDto();

        IntentResultDto? intentResult = null;

        // 🔥 SPRINT 6 y 7 (P0): PENDING ACTION RESOLVER
        // Evitamos llamar a Gemini si el usuario solo está respondiendo a una pregunta pendiente
        if (!string.IsNullOrEmpty(context.PendingAction))
        {
            intentResult = await TryResolvePendingActionAsync(workspaceId, request.MessageText, context, cancellationToken);
        }

        // Si no pudimos resolver la acción pendiente determinísticamente, usamos Gemini
        if (intentResult == null)
        {
            intentResult = await _intentEngine.AnalyzeAsync(request.MessageText, context, cancellationToken);
        }

        if (!intentResult.IsConfident()) intentResult = new IntentResultDto(IntentType.Unknown, 0, new());

        string finalResponse;

        // 🔥 SPRINT 12: Prevención de Exposición de Errores
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
                // 🔥 SPRINT 5 (P0): FAST ROUTER - Cero IA para respuestas estructuradas
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
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Fallo crítico en GeminiAiProvider durante orquestación.");
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
            var extId = await _messageGateway.SendTextAsync(workspaceId, normalizedPhone, finalResponse, cancellationToken);
            await _conversationCache.MarkMessageAsAiGeneratedAsync(workspaceId, extId, cancellationToken);
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversation.Id, pendingId, MessageStatus.Sent, extId, cancellationToken);
        }
        catch
        {
            await _conversationRepo.UpdateMessageStatusAsync(workspaceId, conversation.Id, pendingId, MessageStatus.Failed, null, cancellationToken);
            throw;
        }
    }

    // 🔥 SPRINT 6: Lógica para saltarse a Gemini con Namespace Absoluto para evitar ambigüedad
    private async Task<IntentResultDto?> TryResolvePendingActionAsync(
        Guid workspaceId,
        string userMessage,
        NexFlow.Application.Abstractions.Cache.ConversationContextDto context,
        CancellationToken ct)
    {
        var msg = userMessage.Trim().ToLowerInvariant();

        // Escape rápido: Si el usuario se arrepiente, cancelamos sin gastar IA
        if (msg == "cancelar" || msg == "salir" || msg == "detener")
            return new IntentResultDto(IntentType.CancelReservation, 1.0, new Dictionary<string, string>());

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

        // Si resolvimos el parámetro pendiente, avanzamos en el flujo instantáneamente
        if (parameters.Any())
        {
            return new IntentResultDto(currentIntent, 1.0, parameters);
        }

        return null;
    }

    // 🔥 SPRINT 6: Extracción instantánea de horas
    private string? ExtractTimeFast(string text)
    {
        var match = Regex.Match(text.ToLower(), @"(\d{1,2})(?::(\d{2}))?\s*(am|pm|de la mañana|de la tarde|de la noche)?");
        if (match.Success)
        {
            int hour = int.Parse(match.Groups[1].Value);
            int min = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
            string period = match.Groups[3].Value;

            if (period.Contains("pm") || period.Contains("tarde") || period.Contains("noche"))
                if (hour < 12) hour += 12;
                else if (period.Contains("am") || period.Contains("mañana"))
                    if (hour == 12) hour = 0;

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
                        var dayName = day.GetProperty("day").GetString();
                        var schedule = day.GetProperty("schedule").GetString();
                        sb.AppendLine($"• *{dayName}:* {schedule}");
                    }
                    return sb.ToString();
                }
            }

            if (moduleCode == "REQUESTS" && root.TryGetProperty("status", out var reqStatus))
            {
                if (reqStatus.GetString() == "FOUND")
                {
                    var reqState = root.GetProperty("requestStatus").GetString();
                    var title = root.GetProperty("title").GetString();
                    return $"📄 *Estado de tu trámite*\nTu solicitud de '{title}' se encuentra actualmente en estado: *{reqState}*.";
                }
                if (reqStatus.GetString() == "CREATED")
                {
                    return "✅ Hemos guardado tu solicitud exitosamente. Un asesor revisará tu caso a la brevedad posible.";
                }
            }

            // 🔥 SPRINT 10: Respuesta ultrarrápida (sin IA) para consultas de sedes
            if (moduleCode == "LOCATIONS" && root.TryGetProperty("status", out var locStatus) && locStatus.GetString() == "success")
            {
                if (root.TryGetProperty("locations", out var locArray))
                {
                    var sb = new System.Text.StringBuilder("📍 *Nuestras Sedes:*\n\n");
                    foreach (var loc in locArray.EnumerateArray())
                    {
                        var name = loc.GetProperty("name").GetString();
                        var address = loc.GetProperty("address").GetString();
                        var mapsUrl = loc.TryGetProperty("mapsUrl", out var mUrl) ? mUrl.GetString() : null;

                        sb.AppendLine($"• *{name}:* {address}");
                        if (!string.IsNullOrWhiteSpace(mapsUrl)) sb.AppendLine($"  🗺️ Ver mapa: {mapsUrl}");
                    }
                    return sb.ToString();
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }
}