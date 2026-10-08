using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.API.Middleware;

public class TenantIsolationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantIsolationMiddleware> _logger;

    public TenantIsolationMiddleware(RequestDelegate next, ILogger<TenantIsolationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IMembershipRepository membershipRepo,
        ISystemAdministratorRepository administrators, IWorkspaceRepository workspaces, IServiceScopeFactory scopes)
    {
        if (context.Request.Path.StartsWithSegments("/api/webhooks") ||
            context.GetEndpoint()?.Metadata.GetOrderedMetadata<Microsoft.AspNetCore.Authorization.IAuthorizeData>().Any(a => a.Policy == "SuperAdmin") == true)
        {
            await _next(context);
            return;
        }

        var routeValue = context.Request.RouteValues["workspaceId"]?.ToString();
        var headerValue = context.Request.Headers["X-Workspace-Id"].FirstOrDefault();

        if (Guid.TryParse(routeValue ?? headerValue, out var workspaceId))
        {
            if (currentUser.IsAuthenticated && currentUser.UserId != Guid.Empty)
            {
                var membership = await membershipRepo.GetUserMembershipAsync(currentUser.UserId, workspaceId, context.RequestAborted);
                var isSuperAdmin = await administrators.IsUserSuperAdminAsync(currentUser.UserId, context.RequestAborted);

                if (membership == null && !isSuperAdmin)
                {
                    _logger.LogWarning("[Security] 🔴 Intento BOLA bloqueado. User {UserId} intentó acceder al Workspace {WorkspaceId}", currentUser.UserId, workspaceId);

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsJsonAsync(new { code = "Security.TenantViolation", message = "No tienes permisos para acceder a este entorno de trabajo.", correlationId = context.Items["CorrelationId"]?.ToString() ?? context.TraceIdentifier });
                    return;
                }

                using var lifecycleScope = scopes.CreateScope();
                var lifecycleDb = lifecycleScope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
                await using (var lifecycle = await lifecycleDb.Database.BeginTransactionAsync(context.RequestAborted))
                {
                    await TenantLifecycleLock.AcquireAsync(lifecycleDb, workspaceId, false, context.RequestAborted);
                    var workspace = await workspaces.GetByIdForSuperAdminAsync(workspaceId, context.RequestAborted);
                    if (workspace == null || workspace.Status == NexFlow.Domain.Enums.WorkspaceStatus.Deleting)
                    {
                        context.Response.StatusCode = 409;
                        await context.Response.WriteAsJsonAsync(new { code = "Workspace.Unavailable", message = "El workspace no está disponible.", correlationId = context.TraceIdentifier });
                        return;
                    }
                    if (isSuperAdmin)
                        _logger.LogWarning("SuperAdmin tenant access: UserId {UserId}, WorkspaceId {WorkspaceId}", currentUser.UserId, workspaceId);
                    context.Items["VerifiedWorkspaceId"] = workspaceId;
                    if (context.GetEndpoint()?.Metadata.GetMetadata<ReleaseTenantLifecycleLockAttribute>() == null)
                    {
                        await _next(context);
                        await lifecycle.CommitAsync(context.RequestAborted);
                        return;
                    }
                    // Evolution actions acquire a durable lease separately. Close this transaction before dispatch.
                    await lifecycle.CommitAsync(context.RequestAborted);
                }
                await _next(context);
                return;
            }
        }

        await _next(context);
    }
}
