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
            var header = _httpContextAccessor.HttpContext?.Request.Headers["x-workspace-id"].FirstOrDefault();
            return Guid.TryParse(header, out var workspaceId) ? workspaceId : Guid.Empty;
        }
    }

    public bool HasWorkspace => CurrentWorkspaceId != Guid.Empty;
}