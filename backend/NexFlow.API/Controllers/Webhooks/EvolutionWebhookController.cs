using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using NexFlow.Application.Features.Automation.ProcessMessage;

namespace NexFlow.API.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/evolution")]
public class EvolutionWebhookController : ControllerBase
{
    private readonly ProcessIncomingMessageCommandHandler _handler;

    public EvolutionWebhookController(ProcessIncomingMessageCommandHandler handler)
    {
        _handler = handler;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveMessage(
        [FromBody] EvolutionWebhookPayload payload,
        [FromServices] IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        var expectedApiKey = configuration["Evolution:ApiKey"];

        if (string.IsNullOrEmpty(expectedApiKey))
        {
            return StatusCode(500, new { Error = "Configuración crítica ausente: Evolution:ApiKey no está definido." });
        }

        // 🔥 LECTURA BLINDADA: Busca en Headers (si Evolution lo arregla en el futuro) o en la URL encriptada (TLS).
        var providedApiKey = Request.Headers["apikey"].FirstOrDefault()
                          ?? Request.Headers["ApiKey"].FirstOrDefault()
                          ?? Request.Query["apikey"].FirstOrDefault();

        if (string.IsNullOrEmpty(providedApiKey) || providedApiKey != expectedApiKey)
        {
            return Unauthorized(new { Error = "Acceso denegado. API Key inválida o ausente." });
        }

        var normalizedEvent = payload.Event?.Trim().Replace(".", "_").ToUpperInvariant();
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

        await _handler.Handle(command, cancellationToken);

        return Ok();
    }

    // DTOs con mapeo profesional
    public class EvolutionWebhookPayload
    {
        [JsonPropertyName("event")]
        public string Event { get; set; } = string.Empty;

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
        public string PushName { get; set; } = string.Empty;
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
        public string Conversation { get; set; } = string.Empty;

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
        public string Text { get; set; } = string.Empty;
    }
}