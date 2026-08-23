using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;
using NexFlow.Application.Features.SuperAdmin.Workspaces; // Aseguramos el namespace del Query Handler

namespace NexFlow.API.Controllers.SuperAdmin;

[ApiController]
[Route("api/superadmin/clients")]
[Authorize(Policy = "SuperAdmin")] // 🛡️ ESCUDO ACTIVADO: Cero accesos de clientes normales
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
        // 🛡️ CLEAN ARCHITECTURE: Adiós al DbContext. Delegamos la lectura a la capa Application.
        // Nota: Si tu handler exige un objeto Query vacío, pásale 'new GetSystemWorkspacesQuery()'
        var workspaces = await _getWorkspacesHandler.Handle(cancellationToken);
        return Ok(workspaces);
    }

    [HttpPost("provision")]
    public async Task<IActionResult> ProvisionClient([FromBody] ProvisionClientCommand command, CancellationToken cancellationToken)
    {
        var result = await _provisionHandler.Handle(command, cancellationToken);

        if (result.IsFailure)
        {
            return BadRequest(new { Error = result.Error.Code, Message = result.Error.Description });
        }

        return Created($"/api/workspaces/{result.Value}", new { WorkspaceId = result.Value });
    }
}