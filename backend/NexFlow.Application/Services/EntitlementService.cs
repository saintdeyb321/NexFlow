using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Services;

public class EntitlementService : IEntitlementService
{
    private readonly ILicenseRepository _licenseRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IClock _clock;

    public EntitlementService(
        ILicenseRepository licenseRepository,
        IWorkspaceRepository workspaceRepository,
        IClock clock)
    {
        _licenseRepository = licenseRepository;
        _workspaceRepository = workspaceRepository;
        _clock = clock;
    }

    public async Task<bool> IsLicenseValidAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);
        if (workspace == null || workspace.Status != WorkspaceStatus.Active) return false;

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        if (license == null) return false;

        return license.IsValidAt(_clock.UtcNow);
    }

    public async Task<bool> HasModuleAccessAsync(Guid workspaceId, Guid moduleId, CancellationToken cancellationToken)
    {
        // 1. Primero validamos que el negocio y la licencia estén activos globalmente
        if (!await IsLicenseValidAsync(workspaceId, cancellationToken)) return false;

        // 2. Buscamos si el módulo específico está en su lista
        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);

        return license!.LicenseModules.Any(m => m.ModuleId == moduleId);
    }

    public async Task<IEnumerable<Guid>> GetAvailableModulesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await IsLicenseValidAsync(workspaceId, cancellationToken)) return Enumerable.Empty<Guid>();

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        return license!.LicenseModules.Select(m => m.ModuleId);
    }
}