using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Dispatcher;

public class ModuleDispatcher : IModuleDispatcher
{
    private readonly IEnumerable<IModuleHandler> _moduleHandlers;
    private readonly IEntitlementService _entitlementService;
    private readonly IConversationCache _conversationCache;

    public ModuleDispatcher(
        IEnumerable<IModuleHandler> moduleHandlers,
        IEntitlementService entitlementService,
        IConversationCache conversationCache)
    {
        _moduleHandlers = moduleHandlers;
        _entitlementService = entitlementService;
        _conversationCache = conversationCache;
    }

    public async Task<string> BuildSystemContextAsync(Guid workspaceId, IntentResultDto intentResult, CancellationToken cancellationToken)
    {
        // 1. RECUPERAR MEMORIA A CORTO PLAZO
        var customerPhone = intentResult.Parameters.ContainsKey("phone") ? intentResult.Parameters["phone"] : "unknown";
        var context = await _conversationCache.GetContextAsync(workspaceId, customerPhone, cancellationToken) ?? new ConversationContextDto();

        // 2. FUSIÓN DE CONTEXTO (El escudo contra respuestas cortas)
        if (!string.IsNullOrEmpty(context.PendingAction) && intentResult.Intent == IntentType.Unknown)
        {
            // El cliente respondió algo muy corto como "El Tambo" o "A las 3". 
            // Rescatamos la intención anterior para no romper el flujo de la reserva/solicitud.
            if (Enum.TryParse<IntentType>(context.CurrentIntent, true, out var previousIntent))
            {
                intentResult = new IntentResultDto(previousIntent, 1.0, intentResult.Parameters);
            }
        }
        else if (intentResult.Intent != IntentType.Unknown)
        {
            // Si el cliente cambió de tema drásticamente (ej: estaba reservando pero preguntó "¿Dónde están?"),
            // actualizamos la intención base y borramos acciones pendientes.
            context.CurrentIntent = intentResult.Intent.ToString();
            context.PendingAction = null;
        }

        // Guardamos el contexto actualizado (por si los Handlers quieren leerlo/modificarlo después)
        await _conversationCache.SetContextAsync(workspaceId, customerPhone, context, cancellationToken);

        // 3. TRADUCCIÓN: De Intención a Capacidad
        var capabilityRequest = MapIntentToCapability(intentResult);
        if (capabilityRequest == null)
            return "SISTEMA: Responde cortésmente que no lograste entender la solicitud o pregúntale al cliente si desea que lo derivemos con un agente humano.";

        // 4. LA MURALLA (ENTITLEMENTS)
        bool hasAccess = await _entitlementService.HasCapabilityAccessAsync(
            workspaceId,
            capabilityRequest.ModuleCode,
            capabilityRequest.CapabilityCode,
            cancellationToken);

        if (!hasAccess)
            return $"SISTEMA: El negocio no tiene contratado el módulo de {capabilityRequest.ModuleCode}. Discúlpate amablemente y ofrécele ayuda con las opciones disponibles.";

        // 5. ENRUTAMIENTO SEGURO
        var handler = _moduleHandlers.FirstOrDefault(h => h.ModuleCode == capabilityRequest.ModuleCode);
        if (handler == null || !handler.SupportedCapabilities.Contains(capabilityRequest.CapabilityCode))
            return $"SISTEMA: Error interno. El módulo {capabilityRequest.ModuleCode} no está configurado para manejar la acción {capabilityRequest.CapabilityCode}.";

        // 6. EJECUCIÓN (Aquí pasaremos el contexto a los Handlers en el futuro para operaciones complejas)
        return await handler.ExecuteCapabilityAsync(workspaceId, capabilityRequest, cancellationToken);
    }

    private CapabilityRequest? MapIntentToCapability(IntentResultDto intentResult)
    {
        return intentResult.Intent switch
        {
            IntentType.CheckAvailability => new CapabilityRequest("RESERVATIONS", "CHECK_AVAILABILITY", intentResult.Parameters),
            IntentType.CreateReservation => new CapabilityRequest("RESERVATIONS", "CREATE", intentResult.Parameters),
            IntentType.CancelReservation => new CapabilityRequest("RESERVATIONS", "CANCEL", intentResult.Parameters),
            IntentType.ServiceInformation => new CapabilityRequest("SERVICES", "READ", intentResult.Parameters),
            IntentType.ProductInformation => new CapabilityRequest("CATALOG", "READ", intentResult.Parameters),
            IntentType.CreateRequest => new CapabilityRequest("REQUESTS", "CREATE", intentResult.Parameters),
            IntentType.CheckRequestStatus => new CapabilityRequest("REQUESTS", "UPDATE_STATUS", intentResult.Parameters),
            IntentType.FaqQuery => new CapabilityRequest("FAQ", "READ", intentResult.Parameters),
            IntentType.BusinessProfileQuery => new CapabilityRequest("BUSINESS_PROFILE", "READ", intentResult.Parameters),
            IntentType.LocationQuery => new CapabilityRequest("LOCATIONS", "READ", intentResult.Parameters),
            IntentType.BusinessHoursQuery => new CapabilityRequest("BUSINESS_HOURS", "READ", intentResult.Parameters),
            _ => null
        };
    }
}