using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Requests;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/requests")]
[Authorize(Policy = "WorkspaceMember")]
public class RequestsController : ControllerBase
{
    private readonly IRequestRepository _requestRepository;
    private readonly IRequestService _requestService;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;
    private readonly IMembershipRepository _membershipRepository;

    public RequestsController(
        IRequestRepository requestRepository,
        IRequestService requestService,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService,
        IMembershipRepository membershipRepository)
    {
        _requestRepository = requestRepository;
        _requestService = requestService;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
        _membershipRepository = membershipRepository;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    [HttpGet]
    public async Task<IActionResult> GetRequests(
        [FromQuery] int limit = 50,
        [FromQuery] RequestStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        limit = Math.Clamp(limit, 10, 500);
        var requests = await _requestRepository.GetRequestsAsync(WorkspaceId, limit, status?.ToString(), cancellationToken);

        return Ok(requests);
    }

    [HttpGet("{requestId}")]
    public async Task<IActionResult> GetRequest(string requestId, CancellationToken cancellationToken)
    {
        var modules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!modules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");
        var request = await _requestRepository.GetByIdAsync(WorkspaceId, requestId, cancellationToken);
        return request == null ? NotFound() : Ok(request);
    }

    [HttpPost]
    public async Task<IActionResult> CreateRequest([FromBody] CreateRequestDto payload, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        if (!Enum.TryParse<RequestType>(payload.Type, true, out var parsedType) || !Enum.IsDefined(parsedType))
            return BadRequest(new { code = "Request.InvalidType", message = $"El tipo '{payload.Type}' no es válido." });

        var requestId = await _requestService.CreateRequestAsync(
            WorkspaceId,
            payload.ConsumerPhone,
            payload.ConversationId ?? "MANUAL_ENTRY",
            parsedType,
            payload.Title,
            payload.Description,
            payload.SourceMessageId, // 🔥 SPRINT 08: Idempotencia desde la API
            payload.Metadata,
            cancellationToken);

        return Ok(new { Id = requestId, Message = "Solicitud creada exitosamente." });
    }

    [HttpPut("{requestId}/status")]
    public async Task<IActionResult> UpdateStatus(string requestId, [FromBody] UpdateStatusDto payload, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        if (!Enum.TryParse<RequestStatus>(payload.Status, true, out var parsedStatus) || !Enum.IsDefined(parsedStatus))
            return BadRequest(new { code = "Request.InvalidStatus", message = $"El estado '{payload.Status}' no es válido." });

        var currentRequest = await _requestRepository.GetByIdAsync(WorkspaceId, requestId, cancellationToken);
        if (currentRequest == null) return NotFound();
        if (!RequestRecord.CanTransition(currentRequest.Status, parsedStatus))
            return BadRequest(new { code = "Request.InvalidTransition", message = "Transición de estado no permitida." });
        try { await _requestRepository.UpdateRequestStatusAsync(WorkspaceId, requestId, parsedStatus.ToString(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        catch (InvalidOperationException ex) { return Conflict(new { code = "Request.InvalidTransition", message = ex.Message }); }
        return NoContent();
    }

    [HttpPut("{requestId}/assign")]
    public async Task<IActionResult> AssignRequest(string requestId, [FromBody] AssignRequestDto payload, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        var currentRequest = await _requestRepository.GetByIdAsync(WorkspaceId, requestId, cancellationToken);
        if (currentRequest == null) return NotFound();
        if (!Guid.TryParse(payload.AssignedTo, out var userId) ||
            await _membershipRepository.GetMembershipAsync(WorkspaceId, userId, cancellationToken) == null)
            return BadRequest(new { code = "Request.InvalidAssignee", message = "El usuario debe ser miembro de este workspace." });
        try { await _requestRepository.AssignRequestAsync(WorkspaceId, requestId, userId.ToString(), cancellationToken); }
        catch (KeyNotFoundException) { return NotFound(); }
        return NoContent();
    }
}

public record UpdateStatusDto(string Status);
public record AssignRequestDto(string AssignedTo);
public record CreateRequestDto(
    string ConsumerPhone,
    string? ConversationId,
    string Type,
    string Title,
    string Description,
    string? SourceMessageId,
    Dictionary<string, object>? Metadata
);
