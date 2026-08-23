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

    public ConversationsController(
        IConversationRepository conversationRepository,
        IWorkspaceContext workspaceContext)
    {
        _conversationRepository = conversationRepository;
        _workspaceContext = workspaceContext;
    }
    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    [HttpGet]
    public async Task<IActionResult> GetConversations([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var conversations = await _conversationRepository.GetRecentConversationsAsync(WorkspaceId, limit, cancellationToken);
        return Ok(conversations);
    }
    [HttpGet("{conversationId}/messages")]
    public async Task<IActionResult> GetMessages(string conversationId, [FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        var messages = await _conversationRepository.GetMessagesAsync(WorkspaceId, conversationId, limit, cancellationToken);
        return Ok(messages);
    }
    // 🔥 SPRINT 6: El Escudo Humano (Takeover)
    [HttpPost("{conversationId}/takeover")]
    public async Task<IActionResult> TakeOverConversation(string conversationId, CancellationToken cancellationToken)
    {
        await _conversationRepository.UpdateConversationModeAsync(WorkspaceId, conversationId, ConversationMode.Human, cancellationToken);
        return Ok(new { Message = "Control humano asumido. La IA ha sido silenciada temporalmente.", Mode = ConversationMode.Human.ToString() });
    }
    // 🔥 SPRINT 6: Devolver a la IA (Release)
    [HttpPost("{conversationId}/release")]
    public async Task<IActionResult> ReleaseConversation(string conversationId, CancellationToken cancellationToken)
    {
        await _conversationRepository.UpdateConversationModeAsync(WorkspaceId, conversationId, ConversationMode.Automatic, cancellationToken);
        return Ok(new { Message = "Chat liberado. La Inteligencia Artificial vuelve a tomar el control.", Mode = ConversationMode.Automatic.ToString() });
    }
    // Enviar mensaje manual desde el panel
    [HttpPost("{conversationId}/messages")]
    public async Task<IActionResult> SendManualMessage(
        string conversationId,
        [FromBody] SendManualMessageRequest request,
        [FromServices] IMessageGateway messageGateway,
        CancellationToken cancellationToken)
    {
        var conversation = await _conversationRepository.GetActiveConversationAsync(WorkspaceId, request.ConsumerPhone, cancellationToken);
        if (conversation == null) return NotFound("Conversación no encontrada.");
        // 1. Enviar a Evolution API / WhatsApp
        await messageGateway.SendTextAsync(WorkspaceId, conversation.ConsumerPhone, request.Content, cancellationToken);
        // 2. Guardar en Firestore como "BusinessUser" (Tú)
        var messageRecord = new MessageRecord
        {
            Id = Guid.NewGuid().ToString(),
            Direction = "outbound",
            Sender = SenderType.BusinessUser,
            Content = request.Content,
            Timestamp = DateTime.UtcNow,
            ExpiresAt = DateTime.SpecifyKind(DateTime.UtcNow.AddDays(90), DateTimeKind.Utc)
        };
        await _conversationRepository.AddMessageAsync(WorkspaceId, conversation.Id, messageRecord, cancellationToken);
        // 3. Opcional pero recomendado: Si envías un mensaje manual, asumimos el control humano automáticamente
        if (conversation.Mode != ConversationMode.Human)
        {
            await _conversationRepository.UpdateConversationModeAsync(WorkspaceId, conversation.Id, ConversationMode.Human, cancellationToken);
        }
        return Ok(messageRecord);
    }
}

public record SendManualMessageRequest(string ConsumerPhone, string Content);