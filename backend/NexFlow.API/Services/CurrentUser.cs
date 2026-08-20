using System;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using NexFlow.Application.Abstractions;

namespace NexFlow.API.Services;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;

            // Leemos el Guid directamente de la memoria sin bloquear hilos ni ir a la BD
            if (httpContext != null && httpContext.Items.TryGetValue("UserId", out var userIdObj) && userIdObj is Guid userId)
            {
                return userId;
            }

            return Guid.Empty;
        }
    }

    public string Email => _httpContextAccessor.HttpContext?.User.FindFirst("email")?.Value ?? string.Empty;

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;
}