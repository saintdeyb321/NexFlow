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

                // 🔥 CORRECCIÓN: Si no existe por UID, lo buscamos por correo.
                if (user == null && !string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning($"[Auth] 🟡 FirebaseUid {firebaseUid} no enlazado. Buscando email ({email})...");
                    user = await userRepository.GetByEmailAsync(email, context.RequestAborted);

                    if (user != null)
                    {
                        // 🛡️ BLINDAJE EXTRA: Solo lo enlazamos si el usuario de la DB NO tiene un UID diferente asignado.
                        if (string.IsNullOrEmpty(user.FirebaseUid))
                        {
                            user.LinkFirebaseAccount(firebaseUid);
                            await unitOfWork.SaveChangesAsync(context.RequestAborted);
                            _logger.LogInformation($"[Auth] ✅ Enlace completado para DB Id: {user.Id}");
                        }
                        else
                        {
                            _logger.LogError($"[Auth] 🔴 Choque de Identidad. El email {email} ya pertenece al UID {user.FirebaseUid}.");
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync("{\"code\":\"Auth.IdentityConflict\",\"message\":\"Este correo ya está asociado a otra cuenta de Google.\"}");
                            return;
                        }
                    }
                }

                if (user == null)
                {
                    _logger.LogError($"[Auth] 🔴 Acceso Denegado. El correo {email} NO está provisionado.");
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"code\":\"Auth.Unprovisioned\",\"message\":\"Tu cuenta no está registrada en la plataforma. Solicita acceso al administrador.\"}");
                    return;
                }

                if (user.Status != UserStatus.Active)
                {
                    _logger.LogWarning($"[Auth] 🔴 Acceso Denegado. El usuario {email} está {user.Status}.");
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync($"{{\"code\":\"Auth.Suspended\",\"message\":\"Tu cuenta está {user.Status}. Contacta a soporte.\"}}");
                    return;
                }

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