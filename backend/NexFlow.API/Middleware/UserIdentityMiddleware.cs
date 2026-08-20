using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.API.Middleware;

public class UserIdentityMiddleware
{
    private readonly RequestDelegate _next;

    public UserIdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    // El repositorio se inyecta por método (Scoped) para no romper el ciclo de vida del Middleware (Singleton)
    public async Task InvokeAsync(HttpContext context, IUserRepository userRepository)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var firebaseUid = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!string.IsNullOrEmpty(firebaseUid))
            {
                var user = await userRepository.GetByFirebaseUidAsync(firebaseUid, context.RequestAborted);
                if (user != null)
                {
                    // Guardamos el ID interno en la memoria del ciclo de vida del Request
                    context.Items["UserId"] = user.Id;
                }
            }
        }

        await _next(context);
    }
}