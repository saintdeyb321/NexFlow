using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache; // 🔥 Necesario para Redis
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Domain.Enums;

namespace NexFlow.API.Controllers.Automation;

[ApiController]
[Route("api/conversations")]
[Authorize(Policy = "WorkspaceMember")]
public class ConversationController : ControllerBase
{
    private readonly IConversationRepository _conversationRepository;
    private readonly IConversationCache _conversationCache; // 🔥 Inyección del Caché
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;

    public ConversationController(
        IConversationRepository conversationRepository,
        IConversationCache conversationCache,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService)
    {
        _conversationRepository = conversationRepository;
        _conversationCache = conversationCache;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    private async Task<bool> HasAccessToConversations(CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains("CONVERSATIONS");
    }

    [HttpGet]
    public async Task<IActionResult> GetRecentConversations([FromQuery] int limit = 50, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessToConversations(cancellationToken))
            return StatusCode(403, "Módulo CONVERSATIONS no contratado.");

        var conversations = await _conversationRepository.GetRecentConversationsAsync(WorkspaceId, limit, cancellationToken);
        return Ok(conversations);
    }

    [HttpGet("{conversationId}/messages")]
    public async Task<IActionResult> GetMessages(string conversationId, [FromQuery] int limit = 100, CancellationToken cancellationToken = default)
    {
        if (!await HasAccessToConversations(cancellationToken))
            return StatusCode(403, "Módulo CONVERSATIONS no contratado.");

        var conversation = await _conversationRepository.GetConversationAsync(WorkspaceId, conversationId, cancellationToken);
        if (conversation == null) return NotFound("Conversación no encontrada.");

        var messages = await _conversationRepository.GetMessagesAsync(WorkspaceId, conversationId, limit, cancellationToken);
        return Ok(messages.OrderBy(m => m.Timestamp));
    }

    [HttpPost("{conversationId}/takeover")]
    public async Task<IActionResult> HumanTakeover(string conversationId, CancellationToken cancellationToken)
    {
        if (!await HasAccessToConversations(cancellationToken))
            return StatusCode(403, "Módulo CONVERSATIONS no contratado.");

        var conversation = await _conversationRepository.GetConversationAsync(WorkspaceId, conversationId, cancellationToken);
        if (conversation == null) return NotFound();

        // 1. Actualizamos Firestore
        await _conversationRepository.UpdateConversationModeAsync(
            WorkspaceId,
            conversationId,
            ConversationMode.Human,
            HandoffReason.ManualIntervention,
            cancellationToken);

        // 2. 🔥 SPRINT 11: Destruimos la memoria en Redis para silenciar a la IA instantáneamente
        await _conversationCache.DeleteContextAsync(WorkspaceId, conversation.ConsumerPhone, cancellationToken);

        return Ok(new { message = "Control manual asumido. La IA ha sido pausada y su memoria limpiada." });
    }

    [HttpPost("{conversationId}/release")]
    public async Task<IActionResult> ReleaseToAi(string conversationId, CancellationToken cancellationToken)
    {
        if (!await HasAccessToConversations(cancellationToken))
            return StatusCode(403, "Módulo CONVERSATIONS no contratado.");

        var conversation = await _conversationRepository.GetConversationAsync(WorkspaceId, conversationId, cancellationToken);
        if (conversation == null) return NotFound();

        // 1. Devolvemos el control a la IA en Firestore
        await _conversationRepository.UpdateConversationModeAsync(
            WorkspaceId,
            conversationId,
            ConversationMode.Automatic,
            HandoffReason.None,
            cancellationToken);

        // 2. 🔥 SPRINT 11: Por seguridad, nos aseguramos que inicie con una mente en blanco
        await _conversationCache.DeleteContextAsync(WorkspaceId, conversation.ConsumerPhone, cancellationToken);

        return Ok(new { message = "Conversación devuelta a la Inteligencia Artificial." });
    }

    [HttpDelete("{conversationId}")]
    public async Task<IActionResult> DeleteConversation(string conversationId, CancellationToken cancellationToken)
    {
        if (!await HasAccessToConversations(cancellationToken))
            return StatusCode(403, "Módulo CONVERSATIONS no contratado.");

        var conversation = await _conversationRepository.GetConversationAsync(WorkspaceId, conversationId, cancellationToken);
        if (conversation == null) return NotFound();

        await _conversationRepository.DeleteConversationAsync(WorkspaceId, conversationId, cancellationToken);

        // 🔥 Opcional pero recomendado: Limpiar Redis si se borra el chat
        await _conversationCache.DeleteContextAsync(WorkspaceId, conversation.ConsumerPhone, cancellationToken);

        return NoContent();
    }
}