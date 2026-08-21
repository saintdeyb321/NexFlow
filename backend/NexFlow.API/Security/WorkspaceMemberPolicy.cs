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

    public WorkspaceMemberHandler(
        IHttpContextAccessor httpContextAccessor,
        ICurrentUser currentUser,
        IMembershipRepository membershipRepository)
    {
        _httpContextAccessor = httpContextAccessor;
        _currentUser = currentUser;
        _membershipRepository = membershipRepository;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkspaceMemberRequirement requirement)
    {
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty) return;

        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return;

        // 1. Prioridad Absoluta: Leer el ID de la URL (Ruta RESTful)
        var routeWorkspaceId = httpContext.Request.RouteValues["workspaceId"]?.ToString();

        // 2. Fallback temporal al Header (por compatibilidad si algún endpoint aún no usa ruta)
        var headerWorkspaceId = httpContext.Request.Headers["X-Workspace-Id"].FirstOrDefault();

        var workspaceIdString = routeWorkspaceId ?? headerWorkspaceId;

        if (string.IsNullOrEmpty(workspaceIdString) || !Guid.TryParse(workspaceIdString, out var workspaceId))
            return;

        // 3. Validación real en PostgreSQL
        var membership = await _membershipRepository.GetUserMembershipAsync(userId, workspaceId, System.Threading.CancellationToken.None);

        if (membership != null)
        {
            context.Succeed(requirement);
        }
    }
}