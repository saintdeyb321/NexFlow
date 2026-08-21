using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using NexFlow.Application.Features.Automation.ProcessMessage;

namespace NexFlow.API.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/evolution")]
public class EvolutionWebhookController : ControllerBase
{
    private readonly ProcessIncomingMessageCommandHandler _handler;
    private readonly IDistributedCache _cache; // Usamos el caché global para la idempotencia

    public EvolutionWebhookController(ProcessIncomingMessageCommandHandler handler, IDistributedCache cache)
    {
        _handler = handler;
        _cache = cache;
    }

    [HttpPost]
    public async Task<IActionResult> ReceiveMessage([FromBody] EvolutionWebhookPayload payload, CancellationToken cancellationToken)
    {
        // 1. Validaciones básicas de Evolution
        if (payload?.Data?.Message == null || string.IsNullOrEmpty(payload.Data.Key.Id))
            return Ok(); // Evolution exige un 200 OK rápido para no reintentar

        var messageId = payload.Data.Key.Id;

        // Asumimos que Evolution nos manda el WorkspaceId en la propiedad "Instance"
        if (!Guid.TryParse(payload.Instance, out var workspaceId))
            return Ok();

        // 2. IDEMPOTENCIA: Bloquear duplicados
        var cacheKey = $"webhook:processed:{messageId}";
        var isProcessed = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (!string.IsNullOrEmpty(isProcessed))
            return Ok(); // Ya procesamos este mensaje, ignorarlo

        // Marcamos el mensaje como procesado por 24 horas
        await _cache.SetStringAsync(cacheKey, "true", new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24) }, cancellationToken);

        // 3. Ensamblar y disparar el comando
        var command = new ProcessIncomingMessageCommand(
            WorkspaceId: workspaceId,
            CustomerPhone: payload.Data.Key.RemoteJid.Replace("@s.whatsapp.net", ""),
            CustomerName: payload.Data.PushName ?? "Cliente",
            MessageText: payload.Data.Message.GetRealText(), // <-- USAMOS EL MÉTODO BLINDADO
            MessageId: messageId
        );

        // Nota: En producción extrema, esto debería ir a una cola de mensajes (RabbitMQ/Kafka). 
        // Para este MVP, lo procesamos directamente.
        var result = await _handler.Handle(command, cancellationToken);

        if (result.IsFailure)
        {
            // Podríamos loguear el fallo, pero siempre devolvemos 200 a Evolution para cerrar el HTTP
            return Ok(new { Error = result.Error });
        }

        return Ok();
    }
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