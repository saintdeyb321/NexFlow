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
    private readonly IUserRepository _users;

    public MembersController(IWorkspaceContext workspaceContext, IMembershipRepository memberships, IUserRepository users)
    {
        _workspaceContext = workspaceContext;
        _memberships = memberships;
        _users = users;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    [HttpGet]
    public async Task<IActionResult> GetMembers(CancellationToken cancellationToken)
    {
        var members = await _memberships.GetByWorkspaceIdAsync(WorkspaceId, cancellationToken);
        var result = new List<MemberDto>();
        foreach (var member in members)
        {
            var user = await _users.GetByIdAsync(member.UserId, cancellationToken);
            if (user != null) result.Add(new MemberDto(member.UserId, member.WorkspaceId, member.Role.ToString(), user.FirstName, user.LastName));
        }
        return Ok(result);
    }

    // [HttpPost("invite")]
    // public async Task<IActionResult> InviteMember([FromBody] InviteMemberCommand command, [FromServices] InviteMemberCommandHandler handler, CancellationToken ct) 
    // { 
    //     var result = await handler.Handle(command, ct); ... 
    // }
}

public record MemberDto(Guid UserId, Guid WorkspaceId, string Role, string FirstName, string LastName);
