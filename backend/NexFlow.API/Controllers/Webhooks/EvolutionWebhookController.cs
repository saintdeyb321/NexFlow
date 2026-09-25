using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.API.Services.BackgroundServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Linq;

namespace NexFlow.API.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/evolution")]
public class EvolutionWebhookController : ControllerBase
{
    private readonly IWebhookTaskQueue _taskQueue;
    private readonly ILogger<EvolutionWebhookController> _logger;

    public EvolutionWebhookController(IWebhookTaskQueue taskQueue, ILogger<EvolutionWebhookController> logger)
    {
        _taskQueue = taskQueue;
        _logger = logger;
    }

    [HttpPost]
    [HttpPost("messages-upsert")]
    public async Task<IActionResult> ReceiveMessage(
        [FromBody] EvolutionWebhookPayload payload,
        [FromServices] IConfiguration configuration)
    {
        var expectedWebhookKey = configuration["Evolution:WebhookKey"]?.Trim();

        if (string.IsNullOrEmpty(expectedWebhookKey))
        {
            _logger.LogError("Configuración crítica ausente: Evolution:WebhookKey no está definido.");
            return StatusCode(500, new { Error = "Error interno de servidor." });
        }

        var providedWebhookKey = Request.Headers["X-NexFlow-Webhook-Key"].FirstOrDefault()?.Trim();

        if (string.IsNullOrEmpty(providedWebhookKey) || !string.Equals(providedWebhookKey, expectedWebhookKey, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Webhook authentication failed. Instance={Instance}, Event={Event}", payload?.Instance, payload?.Event);
            return Unauthorized(new { Error = "Acceso denegado. Webhook Key inválida o ausente." });
        }

        var normalizedEvent = payload?.Event?.Trim().Replace(".", "_").ToUpperInvariant();
        if (normalizedEvent != "MESSAGES_UPSERT")
            return Ok(); // Respondemos 200 a otros eventos para que Evolution no marque error

        if (payload?.Data?.Message == null || string.IsNullOrEmpty(payload.Data.Key.Id))
            return Ok();

        // 1. Filtramos Grupos y Broadcasts
        if (payload.Data.Key.RemoteJid.Contains("@g.us") || payload.Data.Key.RemoteJid.Contains("-") || payload.Data.Key.RemoteJid == "status@broadcast")
            return Ok();

        // Si el mensaje fue enviado por el propio negocio, lo descartamos
        if (payload.Data.Key.FromMe)
        {
            _logger.LogDebug("Mensaje saliente (FromMe) ignorado. ID: {MessageId}", payload.Data.Key.Id);
            return Ok();
        }

        var messageText = payload.Data.Message.GetRealText();

        if (string.IsNullOrWhiteSpace(messageText))
        {
            _logger.LogDebug("Mensaje sin texto o contenido no soportado ignorado. ID: {MessageId}", payload.Data.Key.Id);
            return Ok();
        }

        var command = new ProcessIncomingMessageCommand(
            InstanceName: payload.Instance,
            CustomerPhone: payload.Data.Key.RemoteJid.Replace("@s.whatsapp.net", ""),
            CustomerName: payload.Data.PushName ?? "Cliente",
            MessageText: messageText,
            MessageId: payload.Data.Key.Id,
            FromMe: payload.Data.Key.FromMe
        );

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2)); // Evitamos trabar la respuesta HTTP
            await _taskQueue.QueueBackgroundWorkItemAsync(command);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "La cola en memoria está saturada o falló al recibir el mensaje {MessageId}", command.MessageId);
            return StatusCode(503, new { Error = "Servidor saturado temporalmente." });
        }

        return Ok();
    }

    // ==============================================================
    // DTOs Anidados (Mapeo de la estructura JSON de Evolution API)
    // ==============================================================
    public class EvolutionWebhookPayload
    {
        [JsonPropertyName("event")]
        public string? Event { get; set; } = string.Empty;

        [JsonPropertyName("instance")]
        public string Instance { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public EvolutionData? Data { get; set; }
    }

    public class EvolutionData
    {
        [JsonPropertyName("key")]
        public EvolutionKey Key { get; set; } = new();

        [JsonPropertyName("message")]
        public EvolutionMessage Message { get; set; } = new();

        [JsonPropertyName("pushName")]
        public string? PushName { get; set; } = string.Empty;
    }

    public class EvolutionKey
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("remoteJid")]
        public string RemoteJid { get; set; } = string.Empty;

        [JsonPropertyName("fromMe")]
        public bool FromMe { get; set; }
    }

    public class EvolutionMessage
    {
        [JsonPropertyName("conversation")]
        public string? Conversation { get; set; } = string.Empty;

        [JsonPropertyName("extendedTextMessage")]
        public ExtendedTextMessage? ExtendedTextMessage { get; set; }

        // Soportes básicos para que la IA sepa que le enviaron un medio, aunque no pueda leerlo (aún)
        public object? ImageMessage { get; set; }
        public object? AudioMessage { get; set; }
        public object? DocumentMessage { get; set; }

        public string GetRealText()
        {
            if (!string.IsNullOrEmpty(Conversation)) return Conversation;
            if (ExtendedTextMessage != null && !string.IsNullOrEmpty(ExtendedTextMessage.Text)) return ExtendedTextMessage.Text;

            // 🔥 UX para el motor de IA
            if (ImageMessage != null) return "[El cliente envió una imagen]";
            if (AudioMessage != null) return "[El cliente envió un audio]";
            if (DocumentMessage != null) return "[El cliente envió un documento]";

            return string.Empty;
        }
    }

    public class ExtendedTextMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; } = string.Empty;
    }
}