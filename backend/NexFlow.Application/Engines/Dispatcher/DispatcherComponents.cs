using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Engines.Intent.AI;
using NexFlow.Application.Features.Business;
using ConversationContextDto = NexFlow.Application.Abstractions.Cache.ConversationContextDto;

namespace NexFlow.Application.Engines.Dispatcher;

// =====================================================================
// 1. CAPABILITY RESOLVER
// =====================================================================
public interface ICapabilityResolver { CapabilityRequest? Resolve(IntentResultDto intentResult); }

public class CapabilityResolver : ICapabilityResolver
{
    public CapabilityRequest? Resolve(IntentResultDto intentResult) => intentResult.Intent switch
    {
        IntentType.CheckAvailability => new CapabilityRequest("RESERVATIONS", "CHECK_AVAILABILITY", intentResult.Parameters),
        IntentType.CreateReservation => new CapabilityRequest("RESERVATIONS", "CREATE", intentResult.Parameters),
        IntentType.CancelReservation => new CapabilityRequest("RESERVATIONS", "CANCEL", intentResult.Parameters),
        IntentType.ServiceInformation => new CapabilityRequest("SERVICES", "READ", intentResult.Parameters),
        IntentType.ProductInformation => new CapabilityRequest("CATALOG", "READ", intentResult.Parameters),
        IntentType.CreateRequest => new CapabilityRequest("REQUESTS", "CREATE", intentResult.Parameters),
        IntentType.CheckRequestStatus => new CapabilityRequest("REQUESTS", "READ_STATUS", intentResult.Parameters),
        IntentType.FaqQuery => new CapabilityRequest("FAQ", "READ", intentResult.Parameters),
        IntentType.LocationQuery => new CapabilityRequest("LOCATIONS", "READ", intentResult.Parameters),
        IntentType.BusinessHoursQuery => new CapabilityRequest("BUSINESS_HOURS", "READ", intentResult.Parameters),
        IntentType.BusinessProfileQuery => new CapabilityRequest("BUSINESS_PROFILE", "READ", intentResult.Parameters),
        IntentType.GeneralGreeting => new CapabilityRequest("CORE", "GREETING", intentResult.Parameters),
        IntentType.HumanHandoffRequest => new CapabilityRequest("CORE", "TAKEOVER", intentResult.Parameters),
        _ => null
    };
}

// =====================================================================
// 2. CONTEXT RESOLVER
// =====================================================================
public interface ITextNormalizer
{
    Task<string?> GroundLocationAsync(Guid workspaceId, string rawLocationId, CancellationToken ct);
    Task<string?> GroundServiceAsync(Guid workspaceId, string rawServiceId, string? currentLocationId, CancellationToken ct);
    Task<string?> GroundDateAsync(Guid workspaceId, string rawDate, CancellationToken ct);
}

public record ContextResolution(ConversationContextDto Context, ModuleExecutionResult? InterceptResult);

public interface IContextResolver : ITextNormalizer
{
    Task<ContextResolution> EvaluateContextAsync(Guid workspaceId, string customerPhone, IntentResultDto intentResult, CapabilityRequest? capability, CancellationToken ct);
    Task SaveContextAsync(Guid workspaceId, string customerPhone, ConversationContextDto context, CancellationToken ct);
}

public class ContextResolver : IContextResolver
{
    private readonly IConversationCache _cache;
    private readonly ILocationRepository _locationRepo;
    private readonly ICatalogRepository _catalogRepo;
    private readonly IBusinessProfileRepository _profileRepo;
    private readonly IMemoryCache _memoryCache;

    public ContextResolver(
        IConversationCache cache,
        ILocationRepository locationRepo,
        ICatalogRepository catalogRepo,
        IBusinessProfileRepository profileRepo,
        IMemoryCache memoryCache)
    {
        _cache = cache;
        _locationRepo = locationRepo;
        _catalogRepo = catalogRepo;
        _profileRepo = profileRepo;
        _memoryCache = memoryCache;
    }

    private async Task<List<LocationDto>> GetCachedLocationsAsync(Guid workspaceId, CancellationToken ct)
    {
        var cacheKey = $"workspace:{workspaceId}:locations";
        if (!_memoryCache.TryGetValue(cacheKey, out List<LocationDto>? locations))
        {
            locations = (await _locationRepo.GetLocationsAsync(workspaceId, ct)).ToList();
            _memoryCache.Set(cacheKey, locations, TimeSpan.FromMinutes(10));
        }
        return locations ?? new List<LocationDto>();
    }

    private async Task<List<CatalogItemDto>> GetCachedServicesAsync(Guid workspaceId, CancellationToken ct)
    {
        var cacheKey = $"workspace:{workspaceId}:services";
        if (!_memoryCache.TryGetValue(cacheKey, out List<CatalogItemDto>? services))
        {
            var activeItems = await _catalogRepo.GetActiveItemsAsync(workspaceId, ct);
            services = activeItems.Where(i => string.Equals(i.Type, "SERVICE", StringComparison.OrdinalIgnoreCase)).ToList();
            _memoryCache.Set(cacheKey, services, TimeSpan.FromMinutes(10));
        }
        return services ?? new List<CatalogItemDto>();
    }

    private static int ComputeLevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return string.IsNullOrEmpty(t) ? 0 : t.Length;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length, m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) { }
        for (int j = 0; j <= m; d[0, j] = j++) { }

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
            }
        }
        return d[n, m];
    }

    private async Task<TimeZoneInfo> GetWorkspaceTimeZoneAsync(Guid workspaceId, CancellationToken ct)
    {
        var profile = await _profileRepo.GetProfileAsync(workspaceId, ct);
        var tzId = string.IsNullOrWhiteSpace(profile?.TimeZone) ? "America/Lima" : profile.TimeZone;
        try { return TimeZoneInfo.FindSystemTimeZoneById(tzId); }
        catch { return TimeZoneInfo.FindSystemTimeZoneById("America/Lima"); }
    }

    public async Task<string?> GroundDateAsync(Guid workspaceId, string rawDate, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawDate)) return null;

        var normalized = rawDate.Trim().ToLowerInvariant();

        if (DateTime.TryParseExact(normalized, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var exactDate))
            return exactDate.ToString("yyyy-MM-dd");

        var timeZone = await GetWorkspaceTimeZoneAsync(workspaceId, ct);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone).Date;

        if (normalized == "hoy") return localNow.ToString("yyyy-MM-dd");
        if (normalized == "mañana" || normalized == "manana") return localNow.AddDays(1).ToString("yyyy-MM-dd");
        if (normalized == "pasado mañana" || normalized == "pasado manana") return localNow.AddDays(2).ToString("yyyy-MM-dd");

        if (DateTime.TryParse(normalized, out var parsedDate))
        {
            if (parsedDate.Year < localNow.Year) parsedDate = new DateTime(localNow.Year, parsedDate.Month, parsedDate.Day);
            return parsedDate.ToString("yyyy-MM-dd");
        }

        return null;
    }

    public async Task<string?> GroundLocationAsync(Guid workspaceId, string rawLocationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawLocationId)) return null;
        var locations = await GetCachedLocationsAsync(workspaceId, ct);

        var exactMatch = locations.FirstOrDefault(l => l.Id == rawLocationId);
        if (exactMatch != null) return exactMatch.Id;

        var normalizedRaw = rawLocationId.ToLowerInvariant();
        if (normalizedRaw.Contains("principal") || normalizedRaw.Contains("centro") || normalizedRaw.Contains("central"))
        {
            var mainLoc = locations.FirstOrDefault(l => l.IsMain);
            if (mainLoc != null) return mainLoc.Id;
        }

        var nameMatch = locations.FirstOrDefault(l => l.Name.ToLowerInvariant().Contains(normalizedRaw) || normalizedRaw.Contains(l.Name.ToLowerInvariant()));
        if (nameMatch != null) return nameMatch.Id;

        var fuzzyMatch = locations
            .Select(l => new { Loc = l, Dist = ComputeLevenshteinDistance(normalizedRaw, l.Name.ToLowerInvariant()) })
            .Where(x => x.Dist <= 2 || (x.Dist <= 3 && x.Loc.Name.Length > 8))
            .OrderBy(x => x.Dist)
            .FirstOrDefault();

        if (fuzzyMatch != null) return fuzzyMatch.Loc.Id;

        return null;
    }

    public async Task<string?> GroundServiceAsync(Guid workspaceId, string rawServiceId, string? currentLocationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawServiceId)) return null;
        var services = await GetCachedServicesAsync(workspaceId, ct);

        var exactMatch = services.FirstOrDefault(s => s.Id == rawServiceId);
        var normalizedRaw = rawServiceId.ToLowerInvariant();

        if (exactMatch == null)
        {
            exactMatch = services.FirstOrDefault(s => s.Name.ToLowerInvariant().Contains(normalizedRaw) || normalizedRaw.Contains(s.Name.ToLowerInvariant()));
        }

        if (exactMatch == null)
        {
            var fuzzyMatch = services
                .Select(s => new
                {
                    Srv = s,
                    Dist = ComputeLevenshteinDistance(normalizedRaw, s.Name.ToLowerInvariant())
                })
                .Where(x => x.Dist <= 2 || (x.Dist <= 4 && x.Srv.Name.Length > 10))
                .OrderBy(x => x.Dist)
                .FirstOrDefault();

            if (fuzzyMatch != null) exactMatch = fuzzyMatch.Srv;
        }

        if (exactMatch != null)
        {
            // 🔥 CORRECCIÓN: Adaptado a la nueva estructura de DTO de Catálogo (Sprints 02/03)
            if (!string.IsNullOrEmpty(currentLocationId) &&
                !string.Equals(exactMatch.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase) &&
                (exactMatch.LocationIds == null || !exactMatch.LocationIds.Contains(currentLocationId)))
            {
                return null; // El servicio existe, pero no está disponible en la sede que el usuario está consultando
            }
            return exactMatch.Id;
        }
        return null;
    }

    public async Task<ContextResolution> EvaluateContextAsync(Guid workspaceId, string customerPhone, IntentResultDto intentResult, CapabilityRequest? capability, CancellationToken ct)
    {
        var context = await _cache.GetContextAsync(workspaceId, customerPhone, ct) ?? new ConversationContextDto();

        if (!string.IsNullOrEmpty(context.PendingAction))
        {
            bool isInterrupt = intentResult.Intent == IntentType.CancelReservation || intentResult.Intent == IntentType.HumanHandoffRequest;
            if (!isInterrupt && Enum.TryParse<IntentType>(context.CurrentIntent, true, out var previousIntent))
                intentResult = new IntentResultDto(previousIntent, 1.0, intentResult.Parameters);
            else if (isInterrupt)
            {
                context.PendingAction = null;
                context.CurrentIntent = intentResult.Intent.ToString();
            }
        }
        else if (intentResult.Intent != IntentType.Unknown && intentResult.Intent != IntentType.Ambiguous)
            context.CurrentIntent = intentResult.Intent.ToString();

        if (intentResult.Parameters.TryGetValue("locationId", out var rawLoc) && !string.IsNullOrWhiteSpace(rawLoc))
        {
            var groundedLocId = await GroundLocationAsync(workspaceId, rawLoc, ct);
            if (groundedLocId != null)
            {
                intentResult.Parameters["locationId"] = groundedLocId;
                context.SelectedLocationId = groundedLocId;
            }
            else intentResult.Parameters.Remove("locationId");
        }
        if (!string.IsNullOrEmpty(context.SelectedLocationId) && !intentResult.Parameters.ContainsKey("locationId"))
            intentResult.Parameters["locationId"] = context.SelectedLocationId;

        if (intentResult.Parameters.TryGetValue("serviceId", out var rawSrv) && !string.IsNullOrWhiteSpace(rawSrv))
        {
            var groundedSrvId = await GroundServiceAsync(workspaceId, rawSrv, context.SelectedLocationId, ct);
            if (groundedSrvId != null)
            {
                intentResult.Parameters["serviceId"] = groundedSrvId;
                context.SelectedServiceId = groundedSrvId;
            }
            else intentResult.Parameters.Remove("serviceId");
        }
        if (!string.IsNullOrEmpty(context.SelectedServiceId) && !intentResult.Parameters.ContainsKey("serviceId"))
            intentResult.Parameters["serviceId"] = context.SelectedServiceId;

        if (intentResult.Parameters.TryGetValue("date", out var rawDate) && !string.IsNullOrWhiteSpace(rawDate))
        {
            var groundedDate = await GroundDateAsync(workspaceId, rawDate, ct);
            if (groundedDate != null)
            {
                intentResult.Parameters["date"] = groundedDate;
                context.PendingDate = groundedDate;
            }
            else intentResult.Parameters.Remove("date");
        }
        if (!string.IsNullOrEmpty(context.PendingDate) && !intentResult.Parameters.ContainsKey("date"))
            intentResult.Parameters["date"] = context.PendingDate;

        if (intentResult.Parameters.TryGetValue("time", out var time) && time != null) context.PendingTime = time.ToString();
        if (!string.IsNullOrEmpty(context.PendingTime) && !intentResult.Parameters.ContainsKey("time")) intentResult.Parameters["time"] = context.PendingTime;

        if (!string.IsNullOrEmpty(context.LocationScope) && !intentResult.Parameters.ContainsKey("locationScope")) intentResult.Parameters["locationScope"] = context.LocationScope;

        if (capability == null)
            return new ContextResolution(context, new ModuleExecutionResult(false, "SYSTEM", "UNKNOWN", "Lo siento, no logré comprender tu consulta. ¿Podrías darme un poco más de detalle?"));

        if (capability.ModuleCode == "CORE")
        {
            if (capability.CapabilityCode == "TAKEOVER") return new ContextResolution(context, new ModuleExecutionResult(true, "CORE", "TAKEOVER", "Un momento, te transferiré con un asesor humano.", true));
            if (capability.CapabilityCode == "GREETING") return new ContextResolution(context, new ModuleExecutionResult(true, "CORE", "GREETING", "¡Hola! Soy el asistente virtual corporativo. ¿En qué te puedo ayudar?"));
        }

        bool requiresLocationStrict = capability.ModuleCode == "RESERVATIONS";
        bool locationIsUseful = capability.ModuleCode is "SERVICES" or "CATALOG" or "BUSINESS_HOURS" or "LOCATIONS";

        if (string.IsNullOrEmpty(context.SelectedLocationId) && (requiresLocationStrict || context.LocationScope != "ALL"))
        {
            var locations = await GetCachedLocationsAsync(workspaceId, ct);
            if (locations.Count == 1)
            {
                context.SelectedLocationId = locations[0].Id;
                if (!string.IsNullOrEmpty(context.SelectedLocationId))
                {
                    capability.Parameters["locationId"] = context.SelectedLocationId;
                }
            }
            else if (locations.Count > 1)
            {
                if (requiresLocationStrict)
                {
                    var compactLocs = locations.Select(l => new { id = l.Id, name = l.Name }).ToList();
                    context.PendingAction = "ASK_LOCATION";
                    return new ContextResolution(context, new ModuleExecutionResult(true, "SYSTEM", "DISAMBIGUATE_LOCATION", $"El negocio tiene varias sedes. Opciones: {JsonSerializer.Serialize(compactLocs)}. Pregunta amablemente en cuál de estas sedes desea reservar.", false, new[] { "locationId" }));
                }
                else if (locationIsUseful)
                {
                    context.LocationScope = "ALL";
                    if (capability.Parameters != null) capability.Parameters["locationScope"] = "ALL";
                }
            }
        }

        return new ContextResolution(context, null);
    }

    public async Task SaveContextAsync(Guid workspaceId, string customerPhone, ConversationContextDto context, CancellationToken ct)
        => await _cache.SetContextAsync(workspaceId, customerPhone, context, ct);
}

// =====================================================================
// 3. MODULE AUTHORIZER
// =====================================================================
public interface IModuleAuthorizer { Task<bool> IsAuthorizedAsync(Guid workspaceId, string moduleCode, string capabilityCode, CancellationToken ct); }

public class ModuleAuthorizer : IModuleAuthorizer
{
    private readonly IEntitlementService _entitlementService;
    public ModuleAuthorizer(IEntitlementService entitlementService) => _entitlementService = entitlementService;

    public async Task<bool> IsAuthorizedAsync(Guid workspaceId, string moduleCode, string capabilityCode, CancellationToken ct)
        => await _entitlementService.HasCapabilityAccessAsync(workspaceId, moduleCode, capabilityCode, ct);
}

// =====================================================================
// 4. MODULE EXECUTOR
// =====================================================================
public interface IModuleExecutor { Task<ModuleExecutionResult> ExecuteAsync(Guid workspaceId, CapabilityRequest request, CancellationToken ct); }

public class ModuleExecutor : IModuleExecutor
{
    private readonly IEnumerable<IModuleHandler> _handlers;
    public ModuleExecutor(IEnumerable<IModuleHandler> handlers) => _handlers = handlers;

    public async Task<ModuleExecutionResult> ExecuteAsync(Guid workspaceId, CapabilityRequest request, CancellationToken ct)
    {
        var handler = _handlers.FirstOrDefault(h => h.ModuleCode == request.ModuleCode);
        if (handler == null || !handler.SupportedCapabilities.Contains(request.CapabilityCode))
            return new ModuleExecutionResult(false, request.ModuleCode, request.CapabilityCode, $"Error interno. El módulo {request.ModuleCode} no está configurado.");

        return await handler.ExecuteCapabilityAsync(workspaceId, request, ct);
    }
}