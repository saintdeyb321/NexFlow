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
        if (context.Request.Path.StartsWithSegments("/api/webhooks"))
        {
            await _next(context);
            return;
        }

        // 🔥 SPRINT 9: Extraemos de la URL o del Header de forma unificada
        var routeValue = context.Request.RouteValues["workspaceId"]?.ToString();
        var headerValue = context.Request.Headers["X-Workspace-Id"].FirstOrDefault();

        if (Guid.TryParse(routeValue ?? headerValue, out var workspaceId))
        {
            if (currentUser.IsAuthenticated && currentUser.UserId != Guid.Empty)
            {
                var membership = await membershipRepo.GetUserMembershipAsync(currentUser.UserId, workspaceId, context.RequestAborted);

                if (membership == null)
                {
                    _logger.LogWarning("[Security] 🔴 Intento BOLA bloqueado. User {UserId} intentó acceder al Workspace {WorkspaceId}", currentUser.UserId, workspaceId);

                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"code\":\"Security.TenantViolation\",\"message\":\"No tienes permisos para acceder a este entorno de trabajo.\"}");
                    return;
                }

                // Guardamos el ID verificado en los Items. Esta será la ÚNICA fuente de verdad.
                context.Items["VerifiedWorkspaceId"] = workspaceId;
            }
        }

        await _next(context);
    }
}