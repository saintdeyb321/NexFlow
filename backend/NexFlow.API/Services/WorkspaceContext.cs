using Microsoft.AspNetCore.Http;
using NexFlow.Application.Abstractions;

namespace NexFlow.API.Services;

public class WorkspaceContext : IWorkspaceContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public WorkspaceContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid CurrentWorkspaceId
    {
        get
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return Guid.Empty;

            // 🔥 SPRINT 9: Ya no leemos Headers ni Rutas. Leemos el sello de aprobación del Middleware.
            if (httpContext.Items.TryGetValue("VerifiedWorkspaceId", out var verifiedId) && verifiedId is Guid workspaceId)
            {
                return workspaceId;
            }

            return Guid.Empty;
        }
    }

    public bool HasWorkspace => CurrentWorkspaceId != Guid.Empty;
}