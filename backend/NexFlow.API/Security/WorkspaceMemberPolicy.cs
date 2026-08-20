using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.API.Security;

// 1. El Requisito
public class WorkspaceMemberRequirement : IAuthorizationRequirement { }

// 2. El Guardia (Validador Multi-Tenant)
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
        // 1. Verificar si el usuario existe en nuestra BD (ID interno válido)
        var userId = _currentUser.UserId;
        if (userId == Guid.Empty) return;

        // 2. Extraer el WorkspaceId que el cliente intenta acceder
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return;

        if (!httpContext.Request.Headers.TryGetValue("X-Workspace-Id", out var workspaceIdString) ||
            !Guid.TryParse(workspaceIdString, out var workspaceId))
        {
            return; // Si no manda el header, se bloquea el acceso
        }

        // 3. Clean Architecture: Consultamos si hay una relación real en la tabla Memberships
        var membership = await _membershipRepository.GetUserMembershipAsync(userId, workspaceId, System.Threading.CancellationToken.None);

        if (membership != null)
        {
            // El usuario pertenece a este negocio. ¡Acceso concedido!
            context.Succeed(requirement);
        }
    }
}