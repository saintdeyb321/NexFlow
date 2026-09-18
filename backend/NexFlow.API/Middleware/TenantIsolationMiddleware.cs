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

    public async Task InvokeAsync(HttpContext context, ICurrentUser currentUser, IMembershipRepository membershipRepo)
    {
        // 1. Omitimos webhooks porque ya están blindados por el IncomingMessageGuard
        if (context.Request.Path.StartsWithSegments("/api/webhooks"))
        {
            await _next(context);
            return;
        }

        // 2. Extraemos el WorkspaceId que el frontend está intentando consultar
        if (context.Request.Headers.TryGetValue("X-Workspace-Id", out var workspaceHeader) &&
            Guid.TryParse(workspaceHeader, out var workspaceId))
        {
            if (currentUser.IsAuthenticated && currentUser.UserId != Guid.Empty)
            {
                // 🔥 CORRECCIÓN: Usando la firma exacta de tu repositorio
                var membership = await membershipRepo.GetUserMembershipAsync(currentUser.UserId, workspaceId, context.RequestAborted);

                if (membership == null)
                {
                    _logger.LogWarning("[Security] 🔴 Intento BOLA bloqueado. User {UserId} intentó acceder al Workspace {WorkspaceId}", currentUser.UserId, workspaceId);

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"code\":\"Security.TenantViolation\",\"message\":\"No tienes permisos para acceder a este entorno de trabajo.\"}");
                    return;
                }

                context.Items["WorkspaceId"] = workspaceId;
            }
        }

        await _next(context);
    }
}