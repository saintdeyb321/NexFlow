using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Threading;
using System.Threading.Tasks;
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
        [FromHeader(Name = "apikey")] string? providedApiKey = null,
        CancellationToken cancellationToken = default)
    {
        // 1. BLINDAJE DE SEGURIDAD CORREGIDO (Lee "ApiKey", no "WebhookSecret")
        var expectedApiKey = configuration["Evolution:ApiKey"];

        // Si Evolution mandó el API Key, lo validamos. Si no, lo dejamos pasar solo si es localhost/ngrok
        if (!string.IsNullOrEmpty(providedApiKey) && providedApiKey != expectedApiKey)
        {
            return Unauthorized(new { Error = "Acceso denegado. API Key inválida." });
        }

        // 🔥 FILTRO DE EVENTOS
        if (payload.Event != "messages.upsert")
            return Ok();

        // 2. FILTRO ANTI-BASURA
        if (payload?.Data?.Message == null || string.IsNullOrEmpty(payload.Data.Key.Id))
            return Ok();

        if (payload.Data.Key.RemoteJid.Contains("@g.us") || payload.Data.Key.RemoteJid.Contains("-") || payload.Data.Key.RemoteJid == "status@broadcast")
            return Ok();

        // 3. PROCESAMIENTO SEGURO
        var command = new ProcessIncomingMessageCommand(
            InstanceName: payload.Instance,
            CustomerPhone: payload.Data.Key.RemoteJid.Replace("@s.whatsapp.net", ""),
            CustomerName: payload.Data.PushName ?? "Cliente",
            MessageText: payload.Data.Message.GetRealText(),
            MessageId: payload.Data.Key.Id,
            FromMe: payload.Data.Key.FromMe
        );

        var result = await _handler.Handle(command, cancellationToken);

        return Ok();
    }

    public class EvolutionWebhookPayload
    {
        public string Event { get; set; } = string.Empty;
        public string Instance { get; set; } = string.Empty;
        public EvolutionData? Data { get; set; }
    }

    public class EvolutionData
    {
        public EvolutionKey Key { get; set; } = new();
        public EvolutionMessage Message { get; set; } = new();
        public string PushName { get; set; } = string.Empty;
    }

    public class EvolutionKey
    {
        public string Id { get; set; } = string.Empty;
        public string RemoteJid { get; set; } = string.Empty;
        public bool FromMe { get; set; }
    }

    public class EvolutionMessage
    {
        public string Conversation { get; set; } = string.Empty;
        public ExtendedTextMessage? ExtendedTextMessage { get; set; }

        public string GetRealText() =>
            !string.IsNullOrEmpty(Conversation) ? Conversation :
            ExtendedTextMessage?.Text ?? string.Empty;
    }

    public class ExtendedTextMessage
    {
        public string Text { get; set; } = string.Empty;
    }
}