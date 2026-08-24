using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/business/members")]
[Authorize(Policy = "WorkspaceMember")]
public class MembersController : ControllerBase
{
    private readonly IWorkspaceContext _workspaceContext;

    public MembersController(IWorkspaceContext workspaceContext)
    {
        _workspaceContext = workspaceContext;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    [HttpGet]
    public async Task<IActionResult> GetMembers(CancellationToken cancellationToken)
    {
        // TODO: Inyectar un QueryHandler que llame a IMembershipRepository.GetByWorkspaceIdAsync()
        return Ok(new { Message = "Endpoint de lectura de miembros listo para ser conectado al Handler." });
    }

    // [HttpPost("invite")]
    // public async Task<IActionResult> InviteMember([FromBody] InviteMemberCommand command, [FromServices] InviteMemberCommandHandler handler, CancellationToken ct) 
    // { 
    //     var result = await handler.Handle(command, ct); ... 
    // }
}