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
    private readonly IMembershipRepository _membershipRepository; // 🔥 SPRINT 12: Inyectado para Aislamiento B2B
    private readonly IClock _clock;
    private readonly IMemoryCache _cache;
    private readonly ICurrentUser _currentUser;
    private readonly ISystemAdministratorRepository _sysAdminRepository;

    private readonly string[] _baseModules = {
        "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "CONVERSATIONS", "FAQ"
    };

    public EntitlementService(
        ILicenseRepository licenseRepository,
        IWorkspaceRepository workspaceRepository,
        IModuleRepository moduleRepository,
        IMembershipRepository membershipRepository,
        IClock clock,
        IMemoryCache cache,
        ICurrentUser currentUser,
        ISystemAdministratorRepository sysAdminRepository)
    {
        _licenseRepository = licenseRepository;
        _workspaceRepository = workspaceRepository;
        _moduleRepository = moduleRepository;
        _membershipRepository = membershipRepository;
        _clock = clock;
        _cache = cache;
        _currentUser = currentUser;
        _sysAdminRepository = sysAdminRepository;
    }

    public void InvalidateWorkspaceCache(Guid workspaceId)
    {
        _cache.Remove($"entitlement_{workspaceId}");
        if (_currentUser != null && _currentUser.UserId != Guid.Empty)
        {
            _cache.Remove($"role_{workspaceId}_{_currentUser.UserId}");
        }
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

    // 🔥 SPRINT 12: Verificación de Aislamiento de Tenant. 
    // Garantiza que un usuario no pueda consultar datos de un Tenant al que no pertenece.
    private async Task<string?> GetUserRoleAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (_currentUser == null || _currentUser.UserId == Guid.Empty) return null;

        var cacheKey = $"role_{workspaceId}_{_currentUser.UserId}";
        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var membership = await _membershipRepository.GetMembershipAsync(workspaceId, _currentUser.UserId, cancellationToken);
            return membership != null ? membership.Role.ToString().ToUpperInvariant() : null;
        });
    }

    private async Task<EntitlementSnapshot> GetSnapshotAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        if (workspaceId == Guid.Empty) return new EntitlementSnapshot();

        var cacheKey = $"entitlement_{workspaceId}";

        return await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            var snapshot = new EntitlementSnapshot();

            var workspace = await _workspaceRepository.GetByIdAsync(workspaceId, cancellationToken);
            if (workspace == null || (workspace.Status != WorkspaceStatus.Active && workspace.Status != WorkspaceStatus.Pending))
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
                return snapshot;
            }

            var license = await _licenseRepository.GetByWorkspaceIdAsync(workspaceId, cancellationToken);
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);

            if (license == null || !license.IsValidAt(_clock.UtcNow))
            {
                snapshot.IsValid = true;
                snapshot.MaxLocations = 1;
                snapshot.ActiveModuleCodes.Add("CONVERSATIONS");
                snapshot.ActiveModuleCodes.Add("FAQ");
                return snapshot;
            }

            foreach (var baseMod in _baseModules) snapshot.ActiveModuleCodes.Add(baseMod);

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

    // =========================================================================
    // MATRIZ DE AUTORIZACIÓN (Capabilities RBAC) - SPRINT 12
    // =========================================================================
    public async Task<bool> HasCapabilityAccessAsync(Guid workspaceId, string moduleCode, string capabilityCode, CancellationToken cancellationToken)
    {
        if (await IsSuperAdminAsync(cancellationToken)) return true;

        // 1. Aislamiento estricto: Si viene de una llamada HTTP, debe tener un Rol válido en el Tenant.
        if (_currentUser == null || _currentUser.UserId == Guid.Empty) return false;

        var userRole = await GetUserRoleAsync(workspaceId, cancellationToken);
        if (string.IsNullOrEmpty(userRole)) return false; // Intento de acceso a Tenant ajeno bloqueado.

        // 2. Validar que la licencia del Negocio permita el módulo
        var snapshot = await GetSnapshotAsync(workspaceId, cancellationToken);
        if (!snapshot.IsValid) return false;

        var code = moduleCode.ToUpperInvariant();
        var cap = capabilityCode.ToUpperInvariant();

        bool hasLicenseCap = _baseModules.Contains(code) ||
            (snapshot.ModuleCapabilities.TryGetValue(code, out var caps) && caps.Contains(cap));

        if (!hasLicenseCap && !_baseModules.Contains(code)) return false;

        // 3. Evaluar Matriz de Permisos (Rol vs Capacidad)
        return EvaluateRoleMatrix(userRole, code, cap);
    }

    private bool EvaluateRoleMatrix(string role, string module, string capability)
    {
        if (role == "OWNER" || role == "ADMIN") return true;

        // Viewers son estrictamente de lectura
        if (role == "VIEWER") return capability == "READ";

        // Agents/Users operan, pero no borran ni configuran negocio
        if (role == "AGENT" || role == "USER")
        {
            if (capability == "READ") return true;

            return module switch
            {
                "CATALOG" => false,
                "SERVICES" => false,
                "RESERVATIONS" => capability is "CHECK_AVAILABILITY" or "CREATE" or "UPDATE" or "CANCEL" or "COMPLETE",
                "ORDERS" => capability is "CREATE" or "UPDATE" or "UPDATE_STATUS",
                "REQUESTS" => capability is "CREATE" or "ASSIGN" or "UPDATE_STATUS",
                "CONVERSATIONS" => capability is "SEND_MESSAGE" or "TAKEOVER" or "RELEASE",
                "BUSINESS_PROFILE" => false,
                "LOCATIONS" => false,
                "BUSINESS_HOURS" => false,
                "FAQ" => false,
                _ => false
            };
        }

        return false;
    }

    // =========================================================================
    // MÉTODOS DE CONSULTA (Adaptados para Webhooks/Background Workers)
    // =========================================================================

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
            return new[] { "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "CONVERSATIONS", "SERVICES", "CATALOG", "FAQ", "REQUESTS", "RESERVATIONS", "ORDERS" };

        // Si es una petición HTTP con usuario logueado, verificamos que pertenezca al Tenant.
        // Si es el Webhook de IA (System call), el UserId es Empty, así que se permite leer los módulos.
        if (_currentUser != null && _currentUser.UserId != Guid.Empty && !await IsSuperAdminAsync(cancellationToken))
        {
            var role = await GetUserRoleAsync(workspaceId, cancellationToken);
            if (string.IsNullOrEmpty(role)) return Enumerable.Empty<string>();
        }

        var snapshot = await GetSnapshotAsync(workspaceId, cancellationToken);
        if (!snapshot.IsValid) return Enumerable.Empty<string>();
        return snapshot.ActiveModuleCodes;
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