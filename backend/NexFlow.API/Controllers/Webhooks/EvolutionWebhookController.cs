using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
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
    public async Task<IActionResult> ReceiveMessage([FromBody] EvolutionWebhookPayload payload, CancellationToken cancellationToken)
    {
        if (payload?.Data?.Message == null || string.IsNullOrEmpty(payload.Data.Key.Id))
            return Ok();

        var command = new ProcessIncomingMessageCommand(
            InstanceName: payload.Instance, // Pasamos el nombre de la instancia, NO un Guid
            CustomerPhone: payload.Data.Key.RemoteJid.Replace("@s.whatsapp.net", ""),
            CustomerName: payload.Data.PushName ?? "Cliente",
            MessageText: payload.Data.Message.GetRealText(),
            MessageId: payload.Data.Key.Id
        );

        // Despachamos al Application Layer
        var result = await _handler.Handle(command, cancellationToken);

        // Siempre devolvemos 200 OK para que Evolution no genere reintentos tóxicos
        return Ok();
    }

    // DTOs básicos para mapear el JSON exacto que envía Evolution API
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
    }

    public class EvolutionMessage
    {
        public string Conversation { get; set; } = string.Empty;
        public ExtendedTextMessage? ExtendedTextMessage { get; set; }

        // Propiedad calculada para obtener el texto real, sea mensaje normal o respuesta (swipe)
        public string GetRealText() =>
            !string.IsNullOrEmpty(Conversation) ? Conversation :
            ExtendedTextMessage?.Text ?? string.Empty;
    }

    public class ExtendedTextMessage
    {
        public string Text { get; set; } = string.Empty;
    }
}