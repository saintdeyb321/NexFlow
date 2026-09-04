using Microsoft.Extensions.Caching.Memory;
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
    private readonly IMemoryCache _cache;
    private readonly ICurrentUser _currentUser;
    private readonly ISystemAdministratorRepository _sysAdminRepository;

    // 🔥 SPRINT 4.2: Agregamos SERVICES, CATALOG, FAQ y RESERVATIONS a los módulos base 
    // para que estén desbloqueados por defecto sin necesidad de sembrarlos en la BD.
    private readonly string[] _baseModules = {
        "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "CONVERSATIONS",
        "SERVICES", "CATALOG", "FAQ", "RESERVATIONS", "REQUESTS"
    };

    public EntitlementService(
        ILicenseRepository licenseRepository,
        IWorkspaceRepository workspaceRepository,
        IModuleRepository moduleRepository,
        IClock clock,
        IMemoryCache cache,
        ICurrentUser currentUser,
        ISystemAdministratorRepository sysAdminRepository)
    {
        _licenseRepository = licenseRepository;
        _workspaceRepository = workspaceRepository;
        _moduleRepository = moduleRepository;
        _clock = clock;
        _cache = cache;
        _currentUser = currentUser;
        _sysAdminRepository = sysAdminRepository;
    }

    public void InvalidateWorkspaceCache(Guid workspaceId)
    {
        _cache.Remove($"entitlement_{workspaceId}");
    }

    private async Task<bool> IsSuperAdminAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_currentUser == null || _currentUser.UserId == Guid.Empty) return false;
            return await _cache.GetOrCreateAsync($"is_superadmin_{_currentUser.UserId}", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return await _sysAdminRepository.IsUserSuperAdminAsync(_currentUser.UserId, cancellationToken);
            });
        }
        catch
        {
            return false;
        }
    }

    private async Task<EntitlementSnapshot> GetSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (workspaceId == Guid.Empty) return new EntitlementSnapshot();

        var cacheKey = $"entitlement_{workspaceId}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);

            var snapshot = new EntitlementSnapshot();

            var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);
            if (workspace == null || (workspace.Status != WorkspaceStatus.Active && workspace.Status != WorkspaceStatus.Pending))
                return snapshot;

            // Agregamos todos los módulos base al snapshot inmediatamente
            foreach (var baseMod in _baseModules)
            {
                snapshot.ActiveModuleCodes.Add(baseMod);
            }

            var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
            if (license == null || !license.IsValidAt(_clock.UtcNow))
            {
                // Aunque no tenga licencia en BD, permitimos los módulos base (incluyendo Servicios/Reservas)
                snapshot.IsValid = true;
                snapshot.MaxLocations = 1; // Default mínimo
                return snapshot;
            }

            snapshot.IsValid = true;
            snapshot.MaxLocations = license.MaxLocations > 0 ? license.MaxLocations : 1;
            var assignedModuleIds = license.LicenseModules.Select(m => m.ModuleId).ToList();

            if (assignedModuleIds.Any())
            {
                var activeModules = await _moduleRepository.GetActiveModulesAsync(assignedModuleIds, cancellationToken);
                foreach (var mod in activeModules)
                {
                    var code = mod.Code.ToUpperInvariant();
                    snapshot.ActiveModuleCodes.Add(code);
                    snapshot.ActiveModuleIds.Add(mod.Id);
                    snapshot.ModuleCapabilities[code] = mod.Capabilities.Select(c => c.Code.ToUpperInvariant()).ToHashSet();
                }
            }

            return snapshot;
        }) ?? new EntitlementSnapshot();
    }

    public async Task<bool> IsLicenseValidAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (await IsSuperAdminAsync(cancellationToken)) return true;
        var snapshot = await GetSnapshotAsync(workspaceId, cancellationToken);
        return snapshot.IsValid;
    }

    public async Task<bool> HasModuleAccessAsync(Guid workspaceId, Guid moduleId, CancellationToken cancellationToken)
    {
        if (await IsSuperAdminAsync(cancellationToken)) return true;
        var snapshot = await GetSnapshotAsync(workspaceId, cancellationToken);
        return snapshot.IsValid && snapshot.ActiveModuleIds.Contains(moduleId);
    }

    public async Task<IEnumerable<Guid>> GetAvailableModulesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(workspaceId, cancellationToken);
        if (!snapshot.IsValid && !await IsSuperAdminAsync(cancellationToken)) return Enumerable.Empty<Guid>();
        return snapshot.ActiveModuleIds;
    }

    public async Task<IEnumerable<string>> GetAvailableModuleCodesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (await IsSuperAdminAsync(cancellationToken))
        {
            return new[] { "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "CONVERSATIONS", "SERVICES", "CATALOG", "FAQ", "REQUESTS", "RESERVATIONS" };
        }

        var snapshot = await GetSnapshotAsync(workspaceId, cancellationToken);
        if (!snapshot.IsValid) return Enumerable.Empty<string>();
        return snapshot.ActiveModuleCodes;
    }

    public async Task<bool> HasCapabilityAccessAsync(Guid workspaceId, string moduleCode, string capabilityCode, CancellationToken cancellationToken)
    {
        if (await IsSuperAdminAsync(cancellationToken)) return true;

        var snapshot = await GetSnapshotAsync(workspaceId, cancellationToken);
        if (!snapshot.IsValid) return false;

        var code = moduleCode.ToUpperInvariant();

        // Si el módulo está en los base (ahora incluye SERVICES, RESERVATIONS, etc), le damos acceso.
        if (_baseModules.Contains(code)) return true;

        if (snapshot.ModuleCapabilities.TryGetValue(code, out var caps))
        {
            return caps.Contains(capabilityCode.ToUpperInvariant());
        }

        return false;
    }

    public async Task<int> GetMaxLocationsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (await IsSuperAdminAsync(cancellationToken)) return 9999;
        var snapshot = await GetSnapshotAsync(workspaceId, cancellationToken);
        return snapshot.MaxLocations;
    }

    private class EntitlementSnapshot
    {
        public bool IsValid { get; set; }
        public int MaxLocations { get; set; }
        public HashSet<string> ActiveModuleCodes { get; set; } = new();
        public HashSet<Guid> ActiveModuleIds { get; set; } = new();
        public Dictionary<string, HashSet<string>> ModuleCapabilities { get; set; } = new();
    }
}