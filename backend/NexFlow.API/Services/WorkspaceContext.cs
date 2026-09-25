using Microsoft.AspNetCore.Http;
using NexFlow.Application.Abstractions;
using System;

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

            // 1. Procesos en segundo plano o sin HTTP
            if (httpContext == null)
                return Guid.Empty;

            // 2. Si el middleware verificó exitosamente el Workspace, lo devolvemos
            if (httpContext.Items.TryGetValue("VerifiedWorkspaceId", out var verifiedId) && verifiedId is Guid workspaceId)
            {
                return workspaceId;
            }

            // 3. SOFT FAIL: Para endpoints globales (como /api/auth o /api/me) que no requieren Workspace,
            // devolvemos Guid.Empty. Los endpoints que SÍ requieren Workspace están protegidos 
            // por [Authorize(Policy = "WorkspaceMember")] y devolverán 403 automáticamente si esto ocurre.
            return Guid.Empty;
        }
    }

    public bool HasWorkspace => _httpContextAccessor.HttpContext?.Items.ContainsKey("VerifiedWorkspaceId") == true;
}