using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Enums;
// Descomenta la siguiente línea si tu IUserRepository.GetByEmailAsync exige un Value Object Email:
// using NexFlow.Domain.ValueObjects;

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
            // 🔥 CORRECCIÓN: Firebase guarda el UID real en "user_id" o "sub". ASP.NET a veces se confunde.
            var firebaseUid = context.User.FindFirst("user_id")?.Value
                           ?? context.User.FindFirst("sub")?.Value
                           ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var email = context.User.FindFirst("email")?.Value
                     ?? context.User.FindFirst(ClaimTypes.Email)?.Value;

            _logger.LogInformation($"[Auth] 🟢 Token JWT Válido recibido. Email: {email} | UID: {firebaseUid}");

            if (!string.IsNullOrEmpty(firebaseUid))
            {
                // 1. Intentamos buscar por Firebase UID (Login recurrente normal)
                var user = await userRepository.GetByFirebaseUidAsync(firebaseUid, context.RequestAborted);

                // 2. 🔥 SOLUCIÓN FALLO CRÍTICO #1: Si no existe el UID, pero tenemos correo, es el PRIMER LOGIN
                if (user == null && !string.IsNullOrEmpty(email))
                {
                    _logger.LogWarning($"[Auth] 🟡 El FirebaseUid {firebaseUid} NO existe. Buscando por email ({email}) para realizar el enlace...");

                    // (Si tu repositorio pide un ValueObject, cambia 'email' por 'new Email(email)')
                    user = await userRepository.GetByEmailAsync(email, context.RequestAborted);

                    if (user != null)
                    {
                        _logger.LogInformation($"[Auth] 🟢 Usuario encontrado por email. Enlazando cuenta de Firebase...");

                        user.LinkFirebaseAccount(firebaseUid);
                        await unitOfWork.SaveChangesAsync(context.RequestAborted);

                        _logger.LogInformation($"[Auth] ✅ Enlace completado exitosamente para el usuario DB Id: {user.Id}");
                    }
                    else
                    {
                        _logger.LogError($"[Auth] 🔴 El correo {email} NO ha sido provisionado por el SuperAdmin. Acceso denegado.");
                    }
                }

                // 3. Validar estado e inyectar el UserId al contexto si el usuario existe
                if (user != null)
                {
                    if (user.Status != UserStatus.Active)
                    {
                        _logger.LogWarning($"[Auth] 🟡 El usuario {email} existe pero NO está Activo (Estado actual: {user.Status}).");
                    }
                    else
                    {
                        _logger.LogInformation($"[Auth] 🟢 Usuario autenticado y validado. Inyectando contexto. DB Id: {user.Id}");
                        context.Items["UserId"] = user.Id;
                    }
                }
            }
        }
        else
        {
            _logger.LogWarning("[Auth] 🔴 La petición llegó a /api/me pero NO tiene un Token Bearer válido o está mal firmado.");
        }

        await _next(context);
    }
}