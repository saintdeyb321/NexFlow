using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Dispatcher;

public class ModuleDispatcher : IModuleDispatcher
{
    private readonly IEnumerable<IModuleHandler> _moduleHandlers;
    private readonly IEntitlementService _entitlementService;

    public ModuleDispatcher(
        IEnumerable<IModuleHandler> moduleHandlers,
        IEntitlementService entitlementService)
    {
        _moduleHandlers = moduleHandlers;
        _entitlementService = entitlementService;
    }

    public async Task<string> BuildSystemContextAsync(Guid workspaceId, IntentResultDto intentResult, CancellationToken cancellationToken)
    {
        // 1. TRADUCCIÓN: De Intención (Mundo Humano) a Capacidad (Mundo Sistema)
        var capabilityRequest = MapIntentToCapability(intentResult);

        if (capabilityRequest == null)
            return "SISTEMA: Responde cortésmente que no lograste entender la solicitud o pregúntale al cliente si desea que lo derivemos con un agente humano.";

        // 2. LA MURALLA (ENTITLEMENTS): ¿Tiene este workspace el permiso (License) para ejecutar esta capacidad exacta?
        bool hasAccess = await _entitlementService.HasCapabilityAccessAsync(
            workspaceId,
            capabilityRequest.ModuleCode,
            capabilityRequest.CapabilityCode,
            cancellationToken);

        if (!hasAccess)
            return $"SISTEMA: El negocio no tiene contratado el módulo de {capabilityRequest.ModuleCode}. Discúlpate amablemente y ofrécele ayuda con las opciones que sí están disponibles.";

        // 3. ENRUTAMIENTO SEGURO AL HANDLER
        var handler = _moduleHandlers.FirstOrDefault(h => h.ModuleCode == capabilityRequest.ModuleCode);
        if (handler == null || !handler.SupportedCapabilities.Contains(capabilityRequest.CapabilityCode))
            return $"SISTEMA: Error interno. El módulo {capabilityRequest.ModuleCode} no está configurado para manejar la acción {capabilityRequest.CapabilityCode}.";

        // 4. EJECUCIÓN (El Handler obtiene los datos limpios de Firestore/Postgres)
        return await handler.ExecuteCapabilityAsync(workspaceId, capabilityRequest, cancellationToken);
    }

    private CapabilityRequest? MapIntentToCapability(IntentResultDto intentResult)
    {
        // Este es el mapa maestro que conecta lo que el cliente quiere con lo que NexFlow puede hacer
        return intentResult.Intent switch
        {
            IntentType.CheckAvailability => new CapabilityRequest("RESERVATIONS", "CHECK_AVAILABILITY", intentResult.Parameters),
            IntentType.CreateReservation => new CapabilityRequest("RESERVATIONS", "CREATE", intentResult.Parameters),
            IntentType.CancelReservation => new CapabilityRequest("RESERVATIONS", "CANCEL", intentResult.Parameters),

            IntentType.ServiceInformation => new CapabilityRequest("SERVICES", "READ", intentResult.Parameters),
            IntentType.ProductInformation => new CapabilityRequest("CATALOG", "READ", intentResult.Parameters),

            IntentType.CreateRequest => new CapabilityRequest("REQUESTS", "CREATE", intentResult.Parameters),
            IntentType.CheckRequestStatus => new CapabilityRequest("REQUESTS", "UPDATE_STATUS", intentResult.Parameters), // Adaptar a "READ_STATUS" a futuro

            IntentType.FaqQuery => new CapabilityRequest("FAQ", "READ", intentResult.Parameters),
            IntentType.BusinessProfileQuery => new CapabilityRequest("BUSINESS_PROFILE", "READ", intentResult.Parameters),
            IntentType.LocationQuery => new CapabilityRequest("LOCATIONS", "READ", intentResult.Parameters),
            IntentType.BusinessHoursQuery => new CapabilityRequest("BUSINESS_HOURS", "READ", intentResult.Parameters),

            _ => null
        };
    }
}