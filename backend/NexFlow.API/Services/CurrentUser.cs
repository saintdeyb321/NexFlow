using System.Security.Claims;
using NexFlow.Application.Abstractions;

namespace NexFlow.API.Services;

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    public string Email => _httpContextAccessor.HttpContext?.User?.FindFirst("email")?.Value
                        ?? string.Empty;

    public Guid UserId
    {
        get
        {
            // Nota: Como Firebase usa un String UID (ej. "abc123XYZ") y nuestra BD usa Guid, 
            // más adelante crearemos un Middleware que busque el Guid en PostgreSQL usando el Email.
            // Por ahora, devolveremos Empty si no se puede parsear.
            var idClaim = _httpContextAccessor.HttpContext?.User?.FindFirst("user_id")?.Value;
            return Guid.TryParse(idClaim, out var id) ? id : Guid.Empty;
        }
    }
}