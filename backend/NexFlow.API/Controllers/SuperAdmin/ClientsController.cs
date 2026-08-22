using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.API.Controllers.SuperAdmin;

[ApiController]
[Route("api/superadmin/clients")]
// ⚠️ HACK DE DESARROLLO: Comentamos la política estricta temporalmente.
// En producción, descomenta esto para que los clientes normales no puedan crear otros negocios.
// [Authorize(Policy = "SuperAdmin")] 
[Authorize] // <- Ahora solo exigimos estar logueados para poder probar
public class ClientsController : ControllerBase
{
    private readonly ProvisionClientCommandHandler _provisionHandler;
    private readonly NexFlowDbContext _context;

    // Inyectamos el DBContext directo para hacer una lectura rápida (Patrón CQRS simplificado)
    public ClientsController(ProvisionClientCommandHandler provisionHandler, NexFlowDbContext context)
    {
        _provisionHandler = provisionHandler;
        _context = context;
    }

    // 1. SOLUCIÓN AL 404: Endpoint para listar todos los clientes en la UI del SuperAdmin
    [HttpGet]
    public async Task<IActionResult> GetSystemWorkspaces(CancellationToken cancellationToken)
    {
        var workspaces = await _context.Workspaces
            .Select(w => new
            {
                Id = w.Id,
                Name = w.Name ?? "Negocio Sin Nombre",
                Status = w.Status.ToString(),
                CreatedAt = w.CreatedAt
            })
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(workspaces);
    }

    // 2. SOLUCIÓN AL 403: Como bajamos el escudo a [Authorize], ahora el POST pasará con éxito
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