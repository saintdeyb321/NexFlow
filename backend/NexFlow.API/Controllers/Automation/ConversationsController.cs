using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
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
    public async Task<IActionResult> GetConversations([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        if (!await CheckCapabilityAsync("READ", cancellationToken)) return StatusCode(403, "No tiene permisos para leer chats.");
        if (limit < 1 || limit > 100) return BadRequest(new { code = "Pagination.Invalid", message = "El límite debe estar entre 1 y 100." });

        var conversations = await _conversationRepository.GetRecentConversationsAsync(WorkspaceId, limit, cancellationToken);
        return Ok(conversations);
    }

    [HttpGet("{conversationId}/messages")]
    public async Task<IActionResult> GetMessages(string conversationId, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        if (!await CheckCapabilityAsync("READ", cancellationToken)) return StatusCode(403, "No tiene permisos para leer chats.");
        if (limit < 1 || limit > 100) return BadRequest(new { code = "Pagination.Invalid", message = "El límite debe estar entre 1 y 100." });

        var messages = await _conversationRepository.GetMessagesAsync(WorkspaceId, conversationId, limit, cancellationToken);
        return Ok(messages);
    }

    [HttpPost("{conversationId}/takeover")]
    public async Task<IActionResult> TakeOverConversation(string conversationId, CancellationToken cancellationToken)
    {
        if (!await CheckCapabilityAsync("TAKEOVER", cancellationToken)) return StatusCode(403, "No tiene permisos para asumir el control humano.");

        // 🔥 Sprint 4.1: ManualIntervention
        await _conversationRepository.UpdateConversationModeAsync(WorkspaceId, conversationId, ConversationMode.Human, HandoffReason.ManualIntervention, cancellationToken);
        return Ok(new { message = "Control humano asumido. La IA ha sido silenciada temporalmente.", mode = ConversationMode.Human.ToString() });
    }

    [HttpPost("{conversationId}/release")]
    public async Task<IActionResult> ReleaseConversation(string conversationId, CancellationToken cancellationToken)
    {
        if (!await CheckCapabilityAsync("TAKEOVER", cancellationToken)) return StatusCode(403, "No tiene permisos para liberar el chat.");

        // 🔥 Sprint 4.1: Se resetea el Handoff a None
        await _conversationRepository.UpdateConversationModeAsync(WorkspaceId, conversationId, ConversationMode.Automatic, HandoffReason.None, cancellationToken);
        return Ok(new { message = "Chat liberado. La Inteligencia Artificial vuelve a tomar el control.", mode = ConversationMode.Automatic.ToString() });
    }

    [HttpPost("{conversationId}/messages")]
    public async Task<IActionResult> SendManualMessage(
        string conversationId,
        [FromBody] SendManualMessageRequest request,
        [FromServices] IMessageGateway messageGateway,
        CancellationToken cancellationToken)
    {
        if (!await CheckCapabilityAsync("SEND_MESSAGE", cancellationToken)) return StatusCode(403, "No tiene permisos para enviar mensajes.");

        var conversation = await _conversationRepository.GetConversationAsync(WorkspaceId, conversationId, cancellationToken);
        if (conversation == null) return NotFound(new { code = "Conversation.NotFound", message = "Conversación no encontrada." });

        // 🔥 Sprint 4.1: Captura de ExternalId y Set a MessageStatus.Sent
        var externalId = await messageGateway.SendTextAsync(WorkspaceId, conversation.ConsumerPhone, request.Content, cancellationToken);

        var messageRecord = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            Direction = "outbound",
            Sender = SenderType.BusinessUser,
            Content = request.Content,
            ExternalMessageId = externalId,
            Status = MessageStatus.Sent,
            Timestamp = DateTime.UtcNow
        };

        await _conversationRepository.AddMessageAsync(WorkspaceId, conversation.Id, messageRecord, cancellationToken);

        if (conversation.Mode != ConversationMode.Human)
        {
            await _conversationRepository.UpdateConversationModeAsync(WorkspaceId, conversation.Id, ConversationMode.Human, HandoffReason.ManualIntervention, cancellationToken);
        }
        return Ok(messageRecord);
    }

    [HttpDelete("{conversationId}")]
    public async Task<IActionResult> DeleteConversation(string conversationId, [FromServices] NexFlow.Application.Abstractions.Cache.IConversationCache conversationCache, CancellationToken cancellationToken)
    {
        if (!await CheckCapabilityAsync("TAKEOVER", cancellationToken)) return StatusCode(403, "No tiene permisos para eliminar conversaciones.");

        var conversation = await _conversationRepository.GetConversationAsync(WorkspaceId, conversationId, cancellationToken);
        if (conversation == null) return NotFound(new { code = "Conversation.NotFound", message = "Conversación no encontrada." });

        await _conversationRepository.DeleteConversationAsync(WorkspaceId, conversationId, cancellationToken);
        await conversationCache.DeleteContextAsync(WorkspaceId, conversation.ConsumerPhone, cancellationToken);
        return NoContent();
    }
}
public record SendManualMessageRequest(string Content);