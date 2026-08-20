using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Enums;

namespace NexFlow.API.Middleware;

public class UserIdentityMiddleware
{
    private readonly RequestDelegate _next;

    public UserIdentityMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IUserRepository userRepository, IUnitOfWork unitOfWork)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var firebaseUid = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var email = context.User.FindFirst(ClaimTypes.Email)?.Value;

            // Firebase incluye un claim booleano de verificación de email
            var emailVerifiedClaim = context.User.FindFirst("email_verified")?.Value;
            bool isEmailVerified = bool.TryParse(emailVerifiedClaim, out var verified) && verified;

            if (!string.IsNullOrEmpty(firebaseUid))
            {
                // 1. Intentamos buscar por FirebaseUid (Camino feliz recurrente)
                var user = await userRepository.GetByFirebaseUidAsync(firebaseUid, context.RequestAborted);

                // 2. Si no existe, pero hay un email verificado, es el PRIMER LOGIN de un usuario aprovisionado
                if (user == null && !string.IsNullOrEmpty(email) && isEmailVerified)
                {
                    user = await userRepository.GetByEmailAsync(email, context.RequestAborted);

                    if (user != null && user.Status == UserStatus.Active)
                    {
                        // Vinculamos la cuenta y guardamos transaccionalmente
                        user.LinkFirebaseAccount(firebaseUid);
                        await unitOfWork.SaveChangesAsync(context.RequestAborted);
                    }
                }

                // 3. Si el usuario existe y está activo, lo inyectamos en el contexto
                if (user != null && user.Status == UserStatus.Active)
                {
                    context.Items["UserId"] = user.Id;
                }
            }
        }

        await _next(context);
    }
}