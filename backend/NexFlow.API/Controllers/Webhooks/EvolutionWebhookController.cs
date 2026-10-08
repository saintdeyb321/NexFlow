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

        if (string.IsNullOrEmpty(payload?.Instance) || string.IsNullOrEmpty(providedWebhookKey)
            || (!KeyEquals(providedWebhookKey, EvolutionConnectionService.InstanceWebhookKey(expectedWebhookKey, payload.Instance))
                && !KeyEquals(providedWebhookKey, expectedWebhookKey)))
            return Unauthorized(new { Error = "Acceso denegado. Webhook Key inválida o ausente." });

        var normalizedEvent = payload.Event?.Trim().Replace(".", "_").ToUpperInvariant();
        if (normalizedEvent == "CONNECTION_UPDATE")
        {
            if (!string.IsNullOrEmpty(payload.Data?.Instance) && payload.Data.Instance != payload.Instance)
                return BadRequest(new { Error = "Instance mismatch." });
            var owner = await instanceResolver.ResolveInstanceAsync(payload.Instance, HttpContext.RequestAborted);
            if (owner is not { } id || id == Guid.Empty) return BadRequest(new { Error = "Unknown instance." });
            await connections.ObserveConnectionAsync(id, payload.Data?.State?.ToLowerInvariant() ?? string.Empty, HttpContext.RequestAborted);
            return Ok();
        }
        if (normalizedEvent != "MESSAGES_UPSERT")
            return Ok();

        if (payload?.Data?.Message == null || payload.Data.Key == null ||
            string.IsNullOrWhiteSpace(payload.Data.Key.Id) || string.IsNullOrWhiteSpace(payload.Instance) ||
            string.IsNullOrWhiteSpace(payload.Data.Key.RemoteJid))
            return BadRequest(new { Error = "Malformed message envelope." });

        if (payload.Data.Key.RemoteJid.Contains("@g.us") || payload.Data.Key.RemoteJid.Contains("-") || payload.Data.Key.RemoteJid == "status@broadcast")
            return Ok();

        var messageText = payload.Data.Message.GetRealText();

        if (string.IsNullOrWhiteSpace(messageText))
            return BadRequest(new { Error = "Unsupported or empty message." });

        var phone = IncomingMessageGuard.NormalizePhone(payload.Data.Key.RemoteJid);
        if (string.IsNullOrEmpty(phone)) return BadRequest(new { Error = "Invalid phone." });
        var workspaceId = await instanceResolver.ResolveInstanceAsync(payload.Instance, HttpContext.RequestAborted);
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
            ObservedAtUtc: DateTime.UtcNow
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
        [JsonPropertyName("message")] public EvolutionMessage Message { get; set; } = new();
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
        public object? ImageMessage { get; set; }
        public object? AudioMessage { get; set; }
        public object? DocumentMessage { get; set; }

        public string GetRealText()
        {
            if (!string.IsNullOrEmpty(Conversation)) return Conversation;
            if (ExtendedTextMessage != null && !string.IsNullOrEmpty(ExtendedTextMessage.Text)) return ExtendedTextMessage.Text;
            if (ImageMessage != null) return "[El cliente envió una imagen]";
            if (AudioMessage != null) return "[El cliente envió un audio]";
            if (DocumentMessage != null) return "[El cliente envió un documento]";
            return string.Empty;
        }
    }
    public class ExtendedTextMessage
    {
        [JsonPropertyName("text")] public string? Text { get; set; } = string.Empty;
    }

    private static bool KeyEquals(string provided, string expected) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
}
