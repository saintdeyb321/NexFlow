using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.API.Security;

public class WorkspaceMemberRequirement : IAuthorizationRequirement { }

public class WorkspaceMemberHandler : AuthorizationHandler<WorkspaceMemberRequirement>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ICurrentUser _currentUser;
    private readonly IMembershipRepository _membershipRepository;
    private readonly ISystemAdministratorRepository _sysAdminRepository; // 🔥 Auditoría

    public WorkspaceMemberHandler(
        IHttpContextAccessor httpContextAccessor,
        ICurrentUser currentUser,
        IMembershipRepository membershipRepository,
        ISystemAdministratorRepository sysAdminRepository)
    {
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
        _membershipRepository = membershipRepository;
        _sysAdminRepository = sysAdminRepository;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkspaceMemberRequirement requirement)
    {
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty) return;

        // 🔥 Auditoría (Sprint 3.3): El SuperAdmin tiene pase libre a cualquier Workspace
        bool isSuperAdmin = await _sysAdminRepository.IsUserSuperAdminAsync(userId, System.Threading.CancellationToken.None);
        if (isSuperAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return;

        var routeWorkspaceId = httpContext.Request.RouteValues["workspaceId"]?.ToString();
        var headerWorkspaceId = httpContext.Request.Headers["X-Workspace-Id"].FirstOrDefault();

        var workspaceIdString = routeWorkspaceId ?? headerWorkspaceId;

        if (string.IsNullOrEmpty(workspaceIdString) || !Guid.TryParse(workspaceIdString, out var workspaceId))
            return;

        var membership = await _membershipRepository.GetUserMembershipAsync(userId, workspaceId, System.Threading.CancellationToken.None);

        if (membership != null)
        {
            context.Succeed(requirement);
        }
    }
}