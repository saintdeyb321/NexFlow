using System;
using System.Security.Claims;
using System.Threading;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;

namespace NexFlow.API.Services;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private Guid? _cachedUserId;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId
    {
        get
        {
            if (_cachedUserId.HasValue) return _cachedUserId.Value;

            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return Guid.Empty;

            var firebaseUid = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(firebaseUid)) return Guid.Empty;

            using var scope = httpContext.RequestServices.CreateScope();
            var userRepository = scope.ServiceProvider.GetRequiredService<IUserRepository>();

            var user = userRepository.GetByFirebaseUidAsync(firebaseUid, CancellationToken.None).GetAwaiter().GetResult();

            _cachedUserId = user?.Id ?? Guid.Empty;
            return _cachedUserId.Value;
        }
    }

    public string Email => _httpContextAccessor.HttpContext?.User.FindFirst("email")?.Value ?? string.Empty;

    // LA SOLUCIÓN AL ERROR: Le preguntamos directamente a la identidad del contexto web
    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}