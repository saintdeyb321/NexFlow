namespace NexFlow.Application.Abstractions;

public interface IEntitlementService
{
    Task<bool> IsLicenseValidAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<bool> HasModuleAccessAsync(Guid workspaceId, Guid moduleId, CancellationToken cancellationToken);
    Task<IEnumerable<Guid>> GetAvailableModulesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<IEnumerable<string>> GetAvailableModuleCodesAsync(Guid workspaceId, CancellationToken cancellationToken);
}