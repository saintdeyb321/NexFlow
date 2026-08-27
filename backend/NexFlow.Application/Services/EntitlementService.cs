using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Services;

public class EntitlementService : IEntitlementService
{
    private readonly ILicenseRepository _licenseRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IModuleRepository _moduleRepository;
    private readonly IClock _clock;

    // 🔥 SPRINT 3: Módulos Base Inquebrantables
    private readonly string[] _baseModules = { "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS" };

    public EntitlementService(
        ILicenseRepository licenseRepository,
        IWorkspaceRepository workspaceRepository,
        IModuleRepository moduleRepository,
        IClock clock)
    {
        _licenseRepository = licenseRepository;
        _workspaceRepository = workspaceRepository;
        _moduleRepository = moduleRepository;
        _clock = clock;
    }

    public async Task<bool> IsLicenseValidAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (workspaceId == Guid.Empty) return false; // 🛡️ Escudo de seguridad

        var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);

        if (workspace == null || (workspace.Status != WorkspaceStatus.Active && workspace.Status != WorkspaceStatus.Pending))
            return false;

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        return license != null && license.IsValidAt(_clock.UtcNow);
    }

    public async Task<bool> HasModuleAccessAsync(Guid workspaceId, Guid moduleId, CancellationToken cancellationToken)
    {
        if (!await IsLicenseValidAsync(workspaceId, cancellationToken)) return false;

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        if (!license!.LicenseModules.Any(m => m.ModuleId == moduleId)) return false;

        var module = await _moduleRepository.GetByIdAsync(moduleId, cancellationToken);
        return module != null && module.Status == ModuleStatus.Active;
    }

    public async Task<IEnumerable<Guid>> GetAvailableModulesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await IsLicenseValidAsync(workspaceId, cancellationToken)) return Enumerable.Empty<Guid>();

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        var assignedModuleIds = license!.LicenseModules.Select(m => m.ModuleId).ToList();

        if (!assignedModuleIds.Any()) return Enumerable.Empty<Guid>();

        var activeModules = await _moduleRepository.GetActiveModulesAsync(assignedModuleIds, cancellationToken);
        return activeModules.Select(m => m.Id);
    }

    public async Task<IEnumerable<string>> GetAvailableModuleCodesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await IsLicenseValidAsync(workspaceId, cancellationToken)) return Enumerable.Empty<string>();

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        var assignedModuleIds = license!.LicenseModules.Select(m => m.ModuleId).ToList();

        var activeModules = assignedModuleIds.Any()
            ? await _moduleRepository.GetActiveModulesAsync(assignedModuleIds, cancellationToken)
            : Enumerable.Empty<Domain.Entities.Module>();

        var codes = activeModules.Select(m => m.Code.ToUpperInvariant()).ToList();
        codes.AddRange(_baseModules);
        return codes.Distinct();
    }

    public async Task<bool> HasCapabilityAccessAsync(Guid workspaceId, string moduleCode, string capabilityCode, CancellationToken cancellationToken)
    {
        if (!await IsLicenseValidAsync(workspaceId, cancellationToken)) return false;
        if (_baseModules.Contains(moduleCode.ToUpperInvariant())) return true;

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        if (license == null || !license.LicenseModules.Any()) return false;

        var assignedModuleIds = license.LicenseModules.Select(m => m.ModuleId).ToList();
        var activeModules = await _moduleRepository.GetActiveModulesAsync(assignedModuleIds, cancellationToken);

        var targetModule = activeModules.FirstOrDefault(m => m.Code == moduleCode.ToUpperInvariant());
        if (targetModule == null) return false;

        return targetModule.Capabilities.Any(c => c.Code == capabilityCode.ToUpperInvariant());
    }

    public async Task<int> GetMaxLocationsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await IsLicenseValidAsync(workspaceId, cancellationToken)) return 0;

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        return license?.MaxLocations ?? 0;
    }
}