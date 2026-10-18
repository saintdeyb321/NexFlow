using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using NexFlow.Application.Features.Automation.ProcessMessage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using System.Linq;
using System.Text.Json;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities.System;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Infrastructure.Gateways;
using System.Security.Cryptography;
using System.Text;

namespace NexFlow.API.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/evolution")]
public class EvolutionWebhookController : ControllerBase
{
    private readonly ILogger<EvolutionWebhookController> _logger;

    public EvolutionWebhookController(ILogger<EvolutionWebhookController> logger)
    {
        _logger = logger;
    }

    [HttpPost]
    [HttpPost("messages-upsert")]
    public async Task<IActionResult> ReceiveMessage(
        [FromBody] EvolutionWebhookPayload payload,
        [FromServices] IConfiguration configuration,
        [FromServices] IInboundMessageRepository inboundRepo,
        [FromServices] IInstanceResolver instanceResolver,
        [FromServices] IEvolutionConnectionService connections)
    {
        var expectedWebhookKey = configuration["Evolution:WebhookKey"]?.Trim();

        if (string.IsNullOrEmpty(expectedWebhookKey))
        {
            _logger.LogError("Configuración crítica ausente: Evolution:WebhookKey no está definido.");
            return StatusCode(500, new { Error = "Error interno de servidor." });
        }

        var providedWebhookKey = Request.Headers["X-NexFlow-Webhook-Key"].FirstOrDefault()?.Trim();

        // Missing configuration preserves existing installations during migration. Explicit false retires the shared key.
        var legacySetting = configuration["Evolution:AllowLegacyWebhookKey"];
        var allowLegacyKey = legacySetting == null || bool.TryParse(legacySetting, out var allowed) && allowed;
        if (!string.IsNullOrWhiteSpace(payload?.Instance) && allowLegacyKey && !string.IsNullOrEmpty(providedWebhookKey)
            && KeyEquals(providedWebhookKey, expectedWebhookKey))
            _logger.LogWarning("Legacy Evolution webhook authentication used for instance {Instance}. Verify its scoped key before retiring legacy authentication.", payload.Instance);
        if (string.IsNullOrWhiteSpace(payload?.Instance) || string.IsNullOrEmpty(providedWebhookKey)
            || (!KeyEquals(providedWebhookKey, EvolutionConnectionService.InstanceWebhookKey(expectedWebhookKey, payload.Instance))
                && !(allowLegacyKey && KeyEquals(providedWebhookKey, expectedWebhookKey))))
            return Unauthorized(new { Error = "Acceso denegado. Webhook Key inválida o ausente." });

        if (!string.IsNullOrEmpty(payload.Data?.Instance) && payload.Data.Instance != payload.Instance)
            return BadRequest(new { Error = "Instance mismatch." });
        var normalizedEvent = payload.Event?.Trim().Replace(".", "_").ToUpperInvariant();
        if (normalizedEvent == "CONNECTION_UPDATE")
        {
            var owner = await instanceResolver.ResolveAuthenticatedInstanceAsync(payload.Instance, HttpContext.RequestAborted);
            if (owner is not { } id || id == Guid.Empty) return BadRequest(new { Error = "Unknown instance." });
            await connections.ObserveConnectionAsync(id, payload.Data?.State?.ToLowerInvariant() ?? string.Empty, HttpContext.RequestAborted);
            return Ok();
        }
        if (normalizedEvent != "MESSAGES_UPSERT")
            return Ok();

        if (payload?.Data?.Message == null || payload.Data.Key == null ||
            string.IsNullOrWhiteSpace(payload.Data.Key.Id) || string.IsNullOrWhiteSpace(payload.Instance) ||
            string.IsNullOrWhiteSpace(payload.Data.Key.RemoteJid) || !payload.Data.Message.HasContent())
            return BadRequest(new { Error = "Malformed message envelope." });

        if (payload.Data.Key.RemoteJid.Contains("@g.us") || payload.Data.Key.RemoteJid.Contains("-") || payload.Data.Key.RemoteJid == "status@broadcast")
            return Ok();

        var messageText = payload.Data.Message.GetRealText();

        var phone = IncomingMessageGuard.NormalizePhone(payload.Data.Key.RemoteJid);
        if (string.IsNullOrEmpty(phone)) return BadRequest(new { Error = "Invalid phone." });
        var workspaceId = await instanceResolver.ResolveAuthenticatedInstanceAsync(payload.Instance, HttpContext.RequestAborted);
        if (!workspaceId.HasValue || workspaceId == Guid.Empty)
            return BadRequest(new { Error = "Unknown instance." });

        var command = new ProcessIncomingMessageCommand(
            InstanceName: payload.Instance,
            CustomerPhone: phone,
            CustomerName: payload.Data.PushName ?? "Cliente",
            MessageText: messageText,
            MessageId: payload.Data.Key.Id,
            FromMe: payload.Data.Key.FromMe,
            WorkspaceId: workspaceId,
            ObservedAtUtc: DateTime.UtcNow,
            Kind: payload.Data.Message.GetContentKind()
        );

        // 🔥 SPRINT 01: Persistencia Transaccional. Guardamos y respondemos rápido.
        var inboundMessage = new InboundMessage
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            ExternalMessageId = payload.Data.Key.Id,
            InstanceName = payload.Instance,
            Phone = command.CustomerPhone,
            PayloadJson = JsonSerializer.Serialize(command),
            Status = InboundMessageStatus.Pending,
            ReceivedAt = DateTime.UtcNow
        };

        await inboundRepo.AddAsync(inboundMessage, HttpContext.RequestAborted);

        // El mensaje está asegurado en disco. Retornamos OK para que Evolution no reintente.
        return Ok();
    }

    // ==============================================================
    // DTOs Anidados
    // ==============================================================
    public class EvolutionWebhookPayload
    {
        [JsonPropertyName("event")] public string? Event { get; set; } = string.Empty;
        [JsonPropertyName("instance")] public string Instance { get; set; } = string.Empty;
        [JsonPropertyName("data")] public EvolutionData? Data { get; set; }
    }
    public class EvolutionData
    {
        [JsonPropertyName("state")] public string? State { get; set; }
        [JsonPropertyName("instance")] public string? Instance { get; set; }
        [JsonPropertyName("key")] public EvolutionKey Key { get; set; } = new();
        [JsonPropertyName("message")] public EvolutionMessage? Message { get; set; }
        [JsonPropertyName("pushName")] public string? PushName { get; set; } = string.Empty;
    }
    public class EvolutionKey
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("remoteJid")] public string RemoteJid { get; set; } = string.Empty;
        [JsonPropertyName("fromMe")] public bool FromMe { get; set; }
    }
    public class EvolutionMessage
    {
        [JsonPropertyName("conversation")] public string? Conversation { get; set; } = string.Empty;
        [JsonPropertyName("extendedTextMessage")] public ExtendedTextMessage? ExtendedTextMessage { get; set; }
        [JsonPropertyName("imageMessage")] public object? ImageMessage { get; set; }
        [JsonPropertyName("audioMessage")] public object? AudioMessage { get; set; }
        [JsonPropertyName("documentMessage")] public object? DocumentMessage { get; set; }
        [JsonPropertyName("stickerMessage")] public object? StickerMessage { get; set; }
        [JsonPropertyName("videoMessage")] public object? VideoMessage { get; set; }
        [JsonPropertyName("buttonsResponseMessage")] public JsonElement? ButtonsResponse { get; set; }
        [JsonPropertyName("templateButtonReplyMessage")] public JsonElement? TemplateResponse { get; set; }
        [JsonPropertyName("listResponseMessage")] public JsonElement? ListResponse { get; set; }
        [JsonPropertyName("interactiveResponseMessage")] public JsonElement? InteractiveResponse { get; set; }
        [JsonExtensionData] public Dictionary<string, JsonElement>? AdditionalContent { get; set; }

        public bool HasContent() => GetContentKind() != IncomingMessageKind.Unsupported
            || ButtonsResponse.HasValue || TemplateResponse.HasValue || ListResponse.HasValue || InteractiveResponse.HasValue
            || AdditionalContent?.Any(p => p.Value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)) == true;

        public IncomingMessageKind GetContentKind()
        {
            if (ImageMessage != null) return IncomingMessageKind.Image;
            if (AudioMessage != null) return IncomingMessageKind.Audio;
            if (DocumentMessage != null) return IncomingMessageKind.Document;
            if (StickerMessage != null) return IncomingMessageKind.Sticker;
            if (VideoMessage != null) return IncomingMessageKind.Video;
            if (!string.IsNullOrWhiteSpace(Conversation) || !string.IsNullOrWhiteSpace(ExtendedTextMessage?.Text) || !string.IsNullOrWhiteSpace(GetInteractiveText())) return IncomingMessageKind.Text;
            if (AdditionalContent?.Any(p => p.Key is "reactionMessage" or "protocolMessage" or "senderKeyDistributionMessage"
                && p.Value.ValueKind == JsonValueKind.Object) == true) return IncomingMessageKind.Control;
            return IncomingMessageKind.Unsupported;
        }

        public string GetRealText()
        {
            return GetContentKind() switch
            {
                IncomingMessageKind.Text => !string.IsNullOrWhiteSpace(Conversation) ? Conversation : !string.IsNullOrWhiteSpace(ExtendedTextMessage?.Text) ? ExtendedTextMessage.Text : GetInteractiveText()!,
                IncomingMessageKind.Image => "[Imagen recibida; contenido no interpretado]",
                IncomingMessageKind.Audio => "[Audio recibido; contenido no interpretado]",
                IncomingMessageKind.Document => "[Documento recibido; contenido no interpretado]",
                IncomingMessageKind.Sticker => "[Sticker recibido; contenido no interpretado]",
                IncomingMessageKind.Video => "[Video recibido; contenido no interpretado]",
                IncomingMessageKind.Control => "[Evento de control de WhatsApp]",
                _ => "[Mensaje no textual recibido; contenido no interpretado]"
            };
        }

        private string? GetInteractiveText()
        {
            static string? Read(JsonElement? value, string property) => value is { ValueKind: JsonValueKind.Object } v
                && v.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            static string? Number(string? value) => value != null && value.Length <= 2 && value.All(char.IsAsciiDigit) ? value : null;
            var text = Read(ButtonsResponse, "selectedDisplayText") ?? Read(TemplateResponse, "selectedDisplayText") ?? Read(ListResponse, "title");
            if (!string.IsNullOrWhiteSpace(text)) return text;
            var id = Read(ButtonsResponse, "selectedButtonId") ?? Read(TemplateResponse, "selectedId");
            if (ListResponse is { ValueKind: JsonValueKind.Object } list && list.TryGetProperty("singleSelectReply", out var selection)) id ??= Read(selection, "selectedRowId");
            if (InteractiveResponse is { ValueKind: JsonValueKind.Object } interactive && interactive.TryGetProperty("nativeFlowResponseMessage", out var native))
            {
                var json = Read(native, "paramsJson");
                if (json?.Length <= 2048)
                {
                    try { using var parameters = JsonDocument.Parse(json); text = Read(parameters.RootElement, "title"); id ??= Read(parameters.RootElement, "id"); }
                    catch (JsonException) { return null; }
                }
            }
            return !string.IsNullOrWhiteSpace(text) ? text : Number(id);
        }
    }
    public class ExtendedTextMessage
    {
        [JsonPropertyName("text")] public string? Text { get; set; } = string.Empty;
    }

    private static bool KeyEquals(string provided, string expected) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}
