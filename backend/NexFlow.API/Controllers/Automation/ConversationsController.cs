using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;
using NexFlow.Application.Features.Automation.Conversations;

namespace NexFlow.API.Controllers.Automation;

[ApiController]
[Route("api/conversations")]
[Authorize(Policy = "WorkspaceMember")]
public class ConversationsController : ControllerBase
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;

    public record SendManualMessageRequest(string Content);

    public ConversationsController(
        IConversationRepository conversationRepository,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService)
    {
        _conversationRepository = conversationRepository;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    private async Task<bool> CheckCapabilityAsync(string capability, CancellationToken ct) =>
        await _entitlementService.HasCapabilityAccessAsync(WorkspaceId, "CONVERSATIONS", capability, ct);

    [HttpGet]
    public async Task<IActionResult> GetConversations([FromQuery] int limit = 50, [FromQuery] DateTimeOffset? after = null,
        [FromQuery] string? afterId = null, CancellationToken cancellationToken = default)
    {
        if (!await CheckCapabilityAsync("READ", cancellationToken)) return StatusCode(403, "No tiene permisos para leer chats.");
        if (limit < 1 || limit > 100) return BadRequest(new { code = "Pagination.Invalid", message = "El límite debe estar entre 1 y 100." });
        if (!ValidCursor(after, afterId)) return BadRequest(new { code = "Pagination.InvalidCursor", message = "El cursor debe ser una fecha UTC válida." });

        var conversations = await _conversationRepository.GetRecentConversationsAsync(WorkspaceId, limit, cancellationToken, after?.UtcDateTime, afterId);
        return Ok(conversations);
    }

    [HttpGet("{conversationId}/messages")]
    public async Task<IActionResult> GetMessages(string conversationId, [FromQuery] int limit = 50, [FromQuery] DateTimeOffset? after = null,
        [FromQuery] string? afterId = null, CancellationToken cancellationToken = default)
    {
        if (!await CheckCapabilityAsync("READ", cancellationToken)) return StatusCode(403, "No tiene permisos para leer chats.");
        if (limit < 1 || limit > 100) return BadRequest(new { code = "Pagination.Invalid", message = "El límite debe estar entre 1 y 100." });
        if (!ValidCursor(after, afterId)) return BadRequest(new { code = "Pagination.InvalidCursor", message = "El cursor debe ser una fecha UTC válida." });
        if (await _conversationRepository.GetConversationAsync(WorkspaceId, conversationId, cancellationToken) == null)
            return NotFound(new { code = "Conversation.NotFound", message = "Conversación no encontrada." });

        var messages = await _conversationRepository.GetMessagesAsync(WorkspaceId, conversationId, limit, cancellationToken, after?.UtcDateTime, afterId);
        return Ok(messages);
    }

    private static bool ValidCursor(DateTimeOffset? after, string? afterId) =>
        (!after.HasValue || after.Value.Offset == TimeSpan.Zero)
        && (afterId == null || (after.HasValue && !string.IsNullOrWhiteSpace(afterId) && afterId.Length <= 200 && !afterId.Contains('/')));

    [HttpPost("{conversationId}/takeover")]
    public async Task<IActionResult> TakeOverConversation(string conversationId, [FromServices] IHumanHandoffService handoffService, CancellationToken cancellationToken)
    {
        if (!await CheckCapabilityAsync("TAKEOVER", cancellationToken)) return StatusCode(403, "No tiene permisos para asumir el control.");

        await handoffService.EscalateToHumanAsync(WorkspaceId, conversationId, HandoffReason.ManualIntervention, cancellationToken);

        return Ok(new { message = "Control humano asumido. La IA ha sido silenciada temporalmente.", mode = ConversationMode.Human.ToString() });
    }

    [HttpPost("{conversationId}/release")]
    public async Task<IActionResult> ReleaseConversation(string conversationId, [FromServices] IHumanHandoffService handoffService, CancellationToken cancellationToken)
    {
        if (!await CheckCapabilityAsync("RELEASE", cancellationToken)) return StatusCode(403, "No tiene permisos para liberar el chat.");

        await handoffService.ReleaseToAutomaticAsync(WorkspaceId, conversationId, cancellationToken);

        return Ok(new { message = "Chat liberado. La Inteligencia Artificial vuelve a tomar el control.", mode = ConversationMode.Automatic.ToString() });
    }

    [HttpPost("{conversationId}/messages")]
    public async Task<IActionResult> SendManualMessage(
        string conversationId,
        [FromBody] SendManualMessageRequest request,
        [FromServices] IOutboundMessageService outboundMessageService,
        [FromServices] IHumanHandoffService handoffService,
        CancellationToken cancellationToken)
    {
        if (!await CheckCapabilityAsync("SEND_MESSAGE", cancellationToken)) return StatusCode(403, "No tiene permisos para enviar mensajes.");

        var conversation = await _conversationRepository.GetConversationAsync(WorkspaceId, conversationId, cancellationToken);
        if (conversation == null) return NotFound(new { code = "Conversation.NotFound", message = "Conversación no encontrada." });

        if (string.IsNullOrWhiteSpace(request.Content)) return BadRequest(new { message = "El mensaje no puede estar vacío." });
        var clientKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (clientKey?.Length > 200) return BadRequest(new { message = "Idempotency-Key demasiado larga." });
        var sourceId = $"manual:{conversationId}:{(string.IsNullOrWhiteSpace(clientKey) ? Guid.NewGuid().ToString("N") : clientKey)}";
        await handoffService.EscalateToHumanAsync(WorkspaceId, conversation.Id, HandoffReason.ManualIntervention, cancellationToken);
        MessageRecord finalRecord;
        try
        {
            finalRecord = await outboundMessageService.SendMessageAsync(
                WorkspaceId, conversation.Id, conversation.ConsumerPhone, request.Content,
                SenderType.BusinessUser, sourceId, cancellationToken);
        }
        catch (HttpRequestException ex) when (ex.StatusCode.HasValue)
        {
            return StatusCode(503, new { code = "Dependency.EvolutionUnavailable", message = "El proveedor no pudo completar el envío.", correlationId = HttpContext.TraceIdentifier });
        }

        if (finalRecord.Status is MessageStatus.Attempting or MessageStatus.UnknownDelivery)
            return Accepted(finalRecord);
        if (finalRecord.Status == MessageStatus.Failed)
            return StatusCode(502, finalRecord);
        return Ok(finalRecord);
    }

    [HttpDelete("{conversationId}")]
    public async Task<IActionResult> DeleteConversation(string conversationId, [FromServices] IContextRecoveryService contextStore, CancellationToken cancellationToken)
    {
        if (!await CheckCapabilityAsync("DELETE", cancellationToken)) return StatusCode(403, "No tiene permisos para eliminar conversaciones.");

        var conversation = await _conversationRepository.GetConversationAsync(WorkspaceId, conversationId, cancellationToken);
        if (conversation == null) return NotFound(new { code = "Conversation.NotFound", message = "Conversación no encontrada." });

        // Keep the conversation/phone available for retry if state or cache deletion fails.
        await contextStore.DeleteContextAsync(WorkspaceId, conversation.ConsumerPhone, cancellationToken);
        await _conversationRepository.DeleteConversationAsync(WorkspaceId, conversationId, cancellationToken);

        return NoContent();
    }
}
