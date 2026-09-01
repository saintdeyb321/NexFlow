using Microsoft.AspNetCore.Mvc;
using System.Text.Json.Serialization;
using NexFlow.Application.Features.Automation.ProcessMessage;
using Microsoft.Extensions.DependencyInjection; // Necesario para crear alcances seguros

namespace NexFlow.API.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/evolution")]
public class EvolutionWebhookController : ControllerBase
{
    private readonly IServiceScopeFactory _scopeFactory;

    // Inyectamos la fábrica de alcances en lugar del manejador directamente
    public EvolutionWebhookController(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    [HttpPost]
    public IActionResult ReceiveMessage(
        [FromBody] EvolutionWebhookPayload payload,
        [FromServices] IConfiguration configuration)
    {
        var expectedWebhookKey = configuration["Evolution:WebhookKey"]?.Trim();

        if (string.IsNullOrEmpty(expectedWebhookKey))
        {
            return StatusCode(500, new { Error = "Configuración crítica ausente: Evolution:WebhookKey no está definido." });
        }

        var providedWebhookKey = (Request.Headers["X-NexFlow-Webhook-Key"].FirstOrDefault()
                              ?? Request.Headers["apikey"].FirstOrDefault()
                              ?? Request.Headers["ApiKey"].FirstOrDefault()
                              ?? Request.Query["apikey"].FirstOrDefault()
                              ?? payload?.ApiKey)?.Trim();

        if (string.IsNullOrEmpty(providedWebhookKey) || !string.Equals(providedWebhookKey, expectedWebhookKey, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("\n🚨 ALERTA DE SEGURIDAD: CLAVES NO COINCIDEN 🚨");
            Console.WriteLine($"El Backend ESPERABA: '{expectedWebhookKey}'");
            Console.WriteLine($"Evolution ENVIÓ:   '{providedWebhookKey}'");
            Console.WriteLine("--------------------------------------------------\n");

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

        // 🔥 PATRÓN FIRE AND FORGET BLINDADO
        // Desacoplamos el proceso pesado de la solicitud HTTP para que Evolution reciba su OK de inmediato.
        _ = Task.Run(async () =>
        {
            // Creamos un ecosistema de memoria completamente nuevo y aislado para esta transacción
            using var scope = _scopeFactory.CreateScope();
            var handler = scope.ServiceProvider.GetRequiredService<ProcessIncomingMessageCommandHandler>();

            try
            {
                // Usamos CancellationToken.None porque este proceso ya no depende de la conexión web
                await handler.Handle(command, CancellationToken.None);
            }
            catch (Exception ex)
            {
                // Si la BD o Gemini fallan internamente, lo registramos pero no colapsamos el webhook
                Console.WriteLine($"\n⚠️ Fallo en el procesamiento de fondo: {ex.Message}\n");
            }
        });

        // Evolution recibe esto inmediatamente y cierra la conexión HTTP feliz.
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

        [JsonPropertyName("apikey")]
        public string? ApiKey { get; set; }
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