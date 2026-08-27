using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;
using NexFlow.Application.Features.SuperAdmin.Workspaces;
using NexFlow.Application.Features.SuperAdmin.Licenses;
using NexFlow.Application.Abstractions.Repositories; // 🔥 Necesario para los catálogos

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

    // 🔥 SOLUCIÓN FALLO #41: Exponer las Plantillas reales
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates([FromServices] ITemplateRepository templateRepository, CancellationToken cancellationToken)
    {
        // Asumiendo que tienes un GetAllAsync o similar. Si se llama distinto en tu repo, ajusta el nombre.
        var templates = await templateRepository.GetAllAsync(cancellationToken);
        return Ok(templates);
    }

    // 🔥 SOLUCIÓN FALLO #41: Exponer los Módulos reales
    [HttpGet("modules")]
    public async Task<IActionResult> GetModules([FromServices] IModuleRepository moduleRepository, CancellationToken cancellationToken)
    {
        // Asumiendo que tienes un GetAllAsync o similar.
        var modules = await moduleRepository.GetAllAsync(cancellationToken);
        return Ok(modules);
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

    [HttpDelete("{workspaceId}")]
    public async Task<IActionResult> DeleteClient(Guid workspaceId, [FromServices] DeleteClientCommandHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.Handle(new DeleteClientCommand(workspaceId), cancellationToken);
        if (result.IsFailure) return BadRequest(result.Error.Description);
        return NoContent();
    }
}