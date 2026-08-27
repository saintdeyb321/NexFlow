using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/requests")]
[Authorize(Policy = "WorkspaceMember")]
public class RequestsController : ControllerBase
{
    private readonly IRequestRepository _requestRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;

    public RequestsController(
        IRequestRepository requestRepository,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService)
    {
        _requestRepository = requestRepository;
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

    [HttpPut("{requestId}/status")]
    public async Task<IActionResult> UpdateStatus(string requestId, [FromBody] UpdateStatusDto payload, CancellationToken cancellationToken)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken);
        if (!activeModules.Contains("REQUESTS")) return StatusCode(403, "Módulo REQUESTS no contratado.");

        // Validamos contra el Enum estricto
        if (!Enum.TryParse<RequestStatusEnum>(payload.Status, true, out var parsedStatus))
        {
            return BadRequest(new { code = "Request.InvalidStatus", message = $"El estado '{payload.Status}' no es válido." });
        }

        await _requestRepository.UpdateRequestStatusAsync(WorkspaceId, requestId, parsedStatus.ToString(), cancellationToken);
        return NoContent();
    }
}

public enum RequestStatusEnum
{
    Pending,
    InReview,
    Approved,
    Rejected,
    Completed,
    Cancelled
}
public record UpdateStatusDto(string Status);