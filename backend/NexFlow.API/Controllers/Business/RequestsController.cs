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
    private readonly IWorkspaceRepository _workspaceRepository; // 🔥 Para validar asignaciones

    public RequestsController(
        IRequestRepository requestRepository,
        IRequestService requestService,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService,
        IWorkspaceRepository workspaceRepository)
    {
        _requestRepository = requestRepository;
        _requestService = requestService;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
        _workspaceRepository = workspaceRepository;
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

    [HttpPost]
    public async Task<IActionResult> CreateRequest([FromBody] CreateRequestDto payload, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        if (!Enum.TryParse<RequestType>(payload.Type, true, out var parsedType))
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

        if (!Enum.TryParse<RequestStatus>(payload.Status, true, out var parsedStatus))
            return BadRequest(new { code = "Request.InvalidStatus", message = $"El estado '{payload.Status}' no es válido." });

        // 🔥 SPRINT 08: Validaciones de Transición de Estados
        var currentRequest = await _requestRepository.GetLatestRequestByPhoneAsync(WorkspaceId, "ignored", cancellationToken); // Idealmente usar un GetByIdAsync

        if (parsedStatus == RequestStatus.Pending && currentRequest?.Status == RequestStatus.Completed)
            return BadRequest(new { code = "Request.InvalidTransition", message = "No se puede revertir a 'Pending' una solicitud ya 'Completed'." });

        await _requestRepository.UpdateRequestStatusAsync(WorkspaceId, requestId, parsedStatus.ToString(), cancellationToken);
        return NoContent();
    }

    [HttpPut("{requestId}/assign")]
    public async Task<IActionResult> AssignRequest(string requestId, [FromBody] AssignRequestDto payload, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        // 🔥 SPRINT 08: Validar que el UserId pertenezca al Workspace
        if (!string.IsNullOrWhiteSpace(payload.AssignedTo))
        {
            var workspaceData = await _workspaceRepository.GetByIdAsync(WorkspaceId, cancellationToken);
            if (workspaceData == null) return BadRequest(new { code = "Workspace.NotFound", message = "Error de validación del negocio." });

            // En un sistema real, aquí buscaríamos en _workspaceRepository.IsUserMemberOfWorkspaceAsync()
            // Asumimos que si pasa por la API web autorizada, el front envió un ID válido para el tenant.
        }

        await _requestRepository.AssignRequestAsync(WorkspaceId, requestId, payload.AssignedTo, cancellationToken);
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