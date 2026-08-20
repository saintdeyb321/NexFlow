using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Services;

public class EntitlementService : IEntitlementService
{
    private readonly ILicenseRepository _licenseRepository;
    private readonly IWorkspaceRepository _workspaceRepository;
    private readonly IModuleRepository _moduleRepository; // <-- Agregamos el repositorio
    private readonly IClock _clock;

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
        var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);
        if (workspace == null || workspace.Status != WorkspaceStatus.Active) return false;

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        if (license == null) return false;

        return license.IsValidAt(_clock.UtcNow);
    }

    public async Task<bool> HasModuleAccessAsync(Guid workspaceId, Guid moduleId, CancellationToken cancellationToken)
    {
        // 1. Validar que la licencia global esté activa
        if (!await IsLicenseValidAsync(workspaceId, cancellationToken)) return false;

        // 2. Verificar que el cliente haya comprado/asignado este módulo
        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        if (!license!.LicenseModules.Any(m => m.ModuleId == moduleId)) return false;

        // 3. NUEVO: Verificar que el módulo en sí no haya sido dado de baja por SuperAdmin
        var module = await _moduleRepository.GetByIdAsync(moduleId, cancellationToken);
        return module != null && module.Status == ModuleStatus.Active;
    }

    public async Task<IEnumerable<Guid>> GetAvailableModulesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (!await IsLicenseValidAsync(workspaceId, cancellationToken)) return Enumerable.Empty<Guid>();

        var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
        var assignedModuleIds = license!.LicenseModules.Select(m => m.ModuleId).ToList();

        if (!assignedModuleIds.Any()) return Enumerable.Empty<Guid>();

        // NUEVO: Filtramos la lista para devolver SÓLO los módulos que siguen activos en el sistema
        var activeModules = await _moduleRepository.GetActiveModulesAsync(assignedModuleIds, cancellationToken);
        return activeModules.Select(m => m.Id);
    }
}