namespace NexFlow.Application.Abstractions;

// El cerebro que determina qué puede usar un Workspace basado en su licencia y módulos
public interface IEntitlementService
{
    Task<bool> IsLicenseValidAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<bool> HasModuleAccessAsync(Guid workspaceId, string moduleCode, CancellationToken cancellationToken);
    Task<IEnumerable<string>> GetAvailableModulesAsync(Guid workspaceId, CancellationToken cancellationToken);
}