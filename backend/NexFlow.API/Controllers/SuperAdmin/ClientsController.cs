using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;
using NexFlow.Application.Features.SuperAdmin.Workspaces;
using NexFlow.Application.Features.SuperAdmin.Licenses;

namespace NexFlow.API.Controllers.SuperAdmin;

[ApiController]
[Route("api/superadmin/clients")]
[Authorize(Policy = "SuperAdmin")]
public class ClientsController : ControllerBase
{
    private readonly ProvisionClientCommandHandler _provisionHandler;
    private readonly GetSystemWorkspacesQueryHandler _getWorkspacesHandler;

    public ClientsController(
        ProvisionClientCommandHandler provisionHandler,
        GetSystemWorkspacesQueryHandler getWorkspacesHandler)
    {
        _provisionHandler = provisionHandler;
        _getWorkspacesHandler = getWorkspacesHandler;
    }

    [HttpGet]
    public async Task<IActionResult> GetSystemWorkspaces(CancellationToken cancellationToken)
    {
        var workspaces = await _getWorkspacesHandler.Handle(cancellationToken);
        return Ok(workspaces);
    }

    [HttpPost("provision")]
    public async Task<IActionResult> ProvisionClient([FromBody] ProvisionClientCommand command, CancellationToken cancellationToken)
    {
        var result = await _provisionHandler.Handle(command, cancellationToken);
        if (result.IsFailure) return BadRequest(new { Error = result.Error.Code, Message = result.Error.Description });
        return Created($"/api/workspaces/{result.Value}", new { WorkspaceId = result.Value });
    }

    [HttpPost("assign-module")]
    public async Task<IActionResult> AssignModule([FromBody] AssignModuleToLicenseCommand command, [FromServices] AssignModuleToLicenseCommandHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(command, cancellationToken);
        if (result.IsFailure) return BadRequest(result.Error.Description);
        return Ok();
    }

    [HttpPost("renew")]
    public async Task<IActionResult> RenewLicense([FromBody] RenewLicenseCommand command, [FromServices] RenewLicenseCommandHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(command, cancellationToken);
        if (result.IsFailure) return BadRequest(result.Error.Description);
        return Ok();
    }

    [HttpPost("suspend")]
    public async Task<IActionResult> SuspendClient([FromBody] SuspendClientCommand command, [FromServices] SuspendClientCommandHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(command, cancellationToken);
        if (result.IsFailure) return BadRequest(result.Error.Description);
        return Ok();
    }

    [HttpPost("reactivate")]
    public async Task<IActionResult> ReactivateClient([FromBody] ReactivateClientCommand command, [FromServices] ReactivateClientCommandHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(command, cancellationToken);
        if (result.IsFailure) return BadRequest(result.Error.Description);
        return Ok();
    }

    // 🔥 CORRECCIÓN APLICADA: Ahora llama a DeleteClientCommand, no al Handler.
    [HttpDelete("{workspaceId}")]
    public async Task<IActionResult> DeleteClient(Guid workspaceId, [FromServices] DeleteClientCommandHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new DeleteClientCommand(workspaceId), cancellationToken);
        if (result.IsFailure) return BadRequest(result.Error.Description);
        return NoContent();
    }
}