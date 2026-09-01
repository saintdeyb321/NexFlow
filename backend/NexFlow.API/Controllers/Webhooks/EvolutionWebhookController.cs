using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.API.Services.BackgroundServices;

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

        // 🔥 Auditoría aplicada: Solo aceptamos el header personalizado X-NexFlow-Webhook-Key. 
        // Eliminados QueryString y Body parameters para evitar brechas.
        var providedWebhookKey = Request.Headers["X-NexFlow-Webhook-Key"].FirstOrDefault()?.Trim();

        if (string.IsNullOrEmpty(providedWebhookKey) || !string.Equals(providedWebhookKey, expectedWebhookKey, StringComparison.OrdinalIgnoreCase))
        {
            // 🔥 Auditoría aplicada: Cero impresión de secretos. Solo metadata[cite: 2].
            _logger.LogWarning("Webhook authentication failed. Instance={Instance}, Event={Event}", payload?.Instance, payload?.Event);
            return Unauthorized(new { Error = "Acceso denegado. Webhook Key inválida o ausente." });
        }

        var normalizedEvent = payload?.Event?.Trim().Replace(".", "_").ToUpperInvariant();
        if (normalizedEvent != "MESSAGES_UPSERT")
            return Ok();

        if (payload?.Data?.Message == null || string.IsNullOrEmpty(payload.Data.Key.Id))
            return Ok();

        if (payload.Data.Key.RemoteJid.Contains("@g.us") || payload.Data.Key.RemoteJid.Contains("-") || payload.Data.Key.RemoteJid == "status@broadcast")
            return Ok();

        var command = new ProcessIncomingMessageCommand(
            InstanceName: payload.Instance,
            CustomerPhone: payload.Data.Key.RemoteJid.Replace("@s.whatsapp.net", ""),
            CustomerName: payload.Data.PushName ?? "Cliente",
            MessageText: payload.Data.Message.GetRealText(),
            MessageId: payload.Data.Key.Id,
            FromMe: payload.Data.Key.FromMe
        );

        // 🔥 Auditoría aplicada: Encolamos de forma segura usando Channel<T> en lugar del inestable Task.Run[cite: 2].
        await _taskQueue.QueueBackgroundWorkItemAsync(command);

        return Ok();
    }

    public class EvolutionWebhookPayload
    {
        [JsonPropertyName("event")]
        public string? Event { get; set; } = string.Empty;

        [JsonPropertyName("instance")]
        public string Instance { get; set; } = string.Empty;

        [JsonPropertyName("data")]
        public EvolutionData? Data { get; set; }

        // Propiedad ApiKey eliminada para cerrar superficie de ataque[cite: 2].
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

        public object? ImageMessage { get; set; }
        public object? AudioMessage { get; set; }
        public object? DocumentMessage { get; set; }

        public string GetRealText()
        {
            if (!string.IsNullOrEmpty(Conversation)) return Conversation;
            if (ExtendedTextMessage != null && !string.IsNullOrEmpty(ExtendedTextMessage.Text)) return ExtendedTextMessage.Text;

            if (ImageMessage != null) return "[Mensaje de Imagen]";
            if (AudioMessage != null) return "[Mensaje de Audio]";
            if (DocumentMessage != null) return "[Documento Adjunto]";

            return string.Empty;
        }
    }

    public class ExtendedTextMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; } = string.Empty;
    }
}