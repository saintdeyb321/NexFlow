using System.Security.Claims;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Enums;

namespace NexFlow.API.Middleware;

public class UserIdentityMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<UserIdentityMiddleware> _logger;

    public UserIdentityMiddleware(RequestDelegate next, ILogger<UserIdentityMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var firebaseUid = context.User.FindFirst("user_id")?.Value
                           ?? context.User.FindFirst("sub")?.Value
                           ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var email = context.User.FindFirst("email")?.Value
                     ?? context.User.FindFirst(ClaimTypes.Email)?.Value;

            _logger.LogInformation($"[Auth] 🟢 Token JWT recibido. Email: {email} | UID: {firebaseUid}");

            if (!string.IsNullOrEmpty(firebaseUid))
            {
                var user = await userRepository.GetByFirebaseUidAsync(firebaseUid, context.RequestAborted);

                // 🔥 PRIMER LOGIN: Enlazamos la cuenta si existe el correo
                if (user == null && !string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning($"[Auth] 🟡 FirebaseUid {firebaseUid} no enlazado. Buscando email ({email})...");
                    user = await userRepository.GetByEmailAsync(email, context.RequestAborted);

                    if (user != null)
                    {
                        user.LinkFirebaseAccount(firebaseUid);
                        await unitOfWork.SaveChangesAsync(context.RequestAborted);
                        _logger.LogInformation($"[Auth] ✅ Enlace completado para DB Id: {user.Id}");
                    }
                }

                // 🛡️ SOLUCIÓN FALLO #38: Cortar acceso a cuentas NO provisionadas
                if (user == null)
                {
                    _logger.LogError($"[Auth] 🔴 Acceso Denegado. El correo {email} NO está provisionado.");
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"code\":\"Auth.Unprovisioned\",\"message\":\"Tu cuenta no está registrada en la plataforma. Solicita acceso al administrador.\"}");
                    return; // ¡Cortamos el pipeline aquí mismo!
                }

                // 🛡️ BLOQUEO DE USUARIOS SUSPENDIDOS/INACTIVOS
                if (user.Status != UserStatus.Active)
                {
                    _logger.LogWarning($"[Auth] 🔴 Acceso Denegado. El usuario {email} está {user.Status}.");
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"code\":\"Auth.Suspended\",\"message\":\"Tu cuenta está {user.Status}. Contacta a soporte.\"}}");
                    return; // ¡Cortamos el pipeline aquí mismo!
                }

                // Si todo es válido, inyectamos la identidad real
                context.Items["UserId"] = user.Id;
            }
            else
            {
                _logger.LogWarning("[Auth] 🔴 Token sin UID válido.");
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }
        }

        await _next(context);
    }
}