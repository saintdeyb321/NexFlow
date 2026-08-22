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
            return "SISTEMA: No logramos comprender la solicitud. Pide al cliente que reformule.";

        // 2. ENRUTAMIENTO POR CAPACIDAD
        var handler = _moduleHandlers.FirstOrDefault(h => h.ModuleCode == capabilityRequest.ModuleCode);
        if (handler == null || !handler.SupportedCapabilities.Contains(capabilityRequest.CapabilityCode))
            return $"SISTEMA: La capacidad {capabilityRequest.CapabilityCode} no está implementada en el módulo {capabilityRequest.ModuleCode}.";

        // 3. AISLAMIENTO POR LICENCIA (Entitlements)
        var availableModules = await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken);
        if (!availableModules.Contains(handler.ModuleCode))
        {
            return $"SISTEMA: El negocio no tiene contratado el módulo de {handler.ModuleCode}. Responde cortésmente que esta función no está disponible por el momento.";
        }

        // 4. EJECUCIÓN DEL MOTOR (El Handler ya no sabe de IA, solo de datos puros)
        return await handler.ExecuteCapabilityAsync(workspaceId, capabilityRequest, cancellationToken);
    }

    private CapabilityRequest? MapIntentToCapability(IntentResultDto intentResult)
    {
        // Este mapper es el único puente entre la IA y las Reglas de Negocio
        return intentResult.Intent switch
        {
            IntentType.CheckAvailability => new CapabilityRequest("RESERVATIONS", "CHECK_AVAILABILITY", intentResult.Parameters),
            IntentType.CreateReservation => new CapabilityRequest("RESERVATIONS", "CREATE", intentResult.Parameters),
            IntentType.Faq => new CapabilityRequest("FAQ", "ANSWER_QUESTION", intentResult.Parameters),
            _ => null
        };
    }
}