using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
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
            // 🔥 CORRECCIÓN: Firebase guarda el UID real en "user_id" o "sub". ASP.NET a veces se confunde.
            var firebaseUid = context.User.FindFirst("user_id")?.Value
                           ?? context.User.FindFirst("sub")?.Value
                           ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var email = context.User.FindFirst("email")?.Value
                     ?? context.User.FindFirst(ClaimTypes.Email)?.Value;

            _logger.LogInformation($"[Auth] 🟢 Token JWT Válido recibido. Email: {email} | UID: {firebaseUid}");

            if (!string.IsNullOrEmpty(firebaseUid))
            {
                var user = await userRepository.GetByFirebaseUidAsync(firebaseUid, context.RequestAborted);

                if (user == null)
                {
                    _logger.LogWarning($"[Auth] 🔴 El FirebaseUid {firebaseUid} NO existe en la base de datos PostgreSQL.");
                }
                else if (user.Status != UserStatus.Active)
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
        else
        {
            _logger.LogWarning("[Auth] 🔴 La petición llegó a /api/me pero NO tiene un Token Bearer válido o está mal firmado.");
        }

        await _next(context);
    }
}