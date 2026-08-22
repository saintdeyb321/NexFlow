using Microsoft.AspNetCore.Mvc;
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
        [FromHeader(Name = "apikey")] string providedApiKey, // <-- NUEVO: Capturamos el Header
        [FromBody] EvolutionWebhookPayload payload,
        [FromServices] IConfiguration configuration,         // <-- NUEVO: Para leer el appsettings.json
        CancellationToken cancellationToken)
    {
        // 1. BLINDAJE DE SEGURIDAD
        var expectedApiKey = configuration["Evolution:WebhookSecret"];
        if (string.IsNullOrEmpty(expectedApiKey) || providedApiKey != expectedApiKey)
        {
            return Unauthorized(new { Error = "Acceso denegado. Webhook Secret inválido." });
        }

        // 2. PROCESAMIENTO
        if (payload?.Data?.Message == null || string.IsNullOrEmpty(payload.Data.Key.Id))
            return Ok();

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
        public bool FromMe { get; set; } // <-- NUEVO
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