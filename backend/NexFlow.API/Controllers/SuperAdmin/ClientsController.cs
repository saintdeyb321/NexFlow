using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;

namespace NexFlow.API.Controllers.SuperAdmin;

[ApiController]
[Route("api/superadmin/clients")]
public class ClientsController : ControllerBase
{
    private readonly ProvisionClientCommandHandler _provisionHandler;

    public ClientsController(ProvisionClientCommandHandler provisionHandler)
    {
        _provisionHandler = provisionHandler;
    }

    [HttpPost("provision")]
    public async Task<IActionResult> ProvisionClient([FromBody] ProvisionClientCommand command, CancellationToken cancellationToken)
    {
        var result = await _provisionHandler.Handle(command, cancellationToken);

        if (result.IsFailure)
        {
            // Retorna HTTP 400 Bad Request si el Dominio o Application fallan
            return BadRequest(new { Error = result.Error.Code, Message = result.Error.Description });
        }

        // Retorna HTTP 201 Created si todo salió perfecto
        return Created($"/api/workspaces/{result.Value}", new { WorkspaceId = result.Value });
    }
}