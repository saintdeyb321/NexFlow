using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/business/members")]
[Authorize(Policy = "WorkspaceMember")]
public class MembersController : ControllerBase
{
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IMembershipRepository _memberships;

    public MembersController(IWorkspaceContext workspaceContext, IMembershipRepository memberships)
    {
        _workspaceContext = workspaceContext;
        _memberships = memberships;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    [HttpGet]
    public async Task<IActionResult> GetMembers(CancellationToken cancellationToken)
    {
        var members = await _memberships.GetByWorkspaceIdAsync(WorkspaceId, cancellationToken);
        return Ok(members.Select(m => new { m.UserId, m.WorkspaceId, Role = m.Role.ToString() }));
    }

    // [HttpPost("invite")]
    // public async Task<IActionResult> InviteMember([FromBody] InviteMemberCommand command, [FromServices] InviteMemberCommandHandler handler, CancellationToken ct) 
    // { 
    //     var result = await handler.Handle(command, ct); ... 
    // }
}
