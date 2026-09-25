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

    public RequestsController(
        IRequestRepository requestRepository,
        IRequestService requestService,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService)
    {
        _requestRepository = requestRepository;
        _requestService = requestService;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    [HttpGet]
    public async Task<IActionResult> GetRequests(CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        var requests = await _requestRepository.GetRequestsAsync(WorkspaceId, cancellationToken);
        return Ok(requests);
    }

    // 🔥 SPRINT 06: Nuevo endpoint para creación manual desde el Dashboard
    [HttpPost]
    public async Task<IActionResult> CreateRequest([FromBody] CreateRequestDto payload, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        if (!Enum.TryParse<RequestType>(payload.Type, true, out var parsedType))
        {
            return BadRequest(new { code = "Request.InvalidType", message = $"El tipo '{payload.Type}' no es válido." });
        }

        var requestId = await _requestService.CreateRequestAsync(
            WorkspaceId,
            payload.ConsumerPhone,
            payload.ConversationId ?? "MANUAL_ENTRY", // Si se crea a mano, indicamos el origen
            parsedType,
            payload.Title,
            payload.Description,
            payload.Metadata,
            cancellationToken);

        return Ok(new { Id = requestId, Message = "Solicitud creada exitosamente." });
    }

    [HttpPut("{requestId}/status")]
    public async Task<IActionResult> UpdateStatus(string requestId, [FromBody] UpdateStatusDto payload, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        if (!Enum.TryParse<RequestStatus>(payload.Status, true, out var parsedStatus))
        {
            return BadRequest(new { code = "Request.InvalidStatus", message = $"El estado '{payload.Status}' no es válido." });
        }

        await _requestRepository.UpdateRequestStatusAsync(WorkspaceId, requestId, parsedStatus.ToString(), cancellationToken);
        return NoContent();
    }
}

// DTOs
public record UpdateStatusDto(string Status);

public record CreateRequestDto(
    string ConsumerPhone,
    string? ConversationId,
    string Type,
    string Title,
    string Description,
    Dictionary<string, object>? Metadata
);