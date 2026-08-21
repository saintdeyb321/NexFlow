using System;
using System.Linq;
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

            var routeValue = httpContext.Request.RouteValues["workspaceId"]?.ToString();
            var headerValue = httpContext.Request.Headers["X-Workspace-Id"].FirstOrDefault();

            return Guid.TryParse(routeValue ?? headerValue, out var workspaceId) ? workspaceId : Guid.Empty;
        }
    }

    public bool HasWorkspace => CurrentWorkspaceId != Guid.Empty;
}