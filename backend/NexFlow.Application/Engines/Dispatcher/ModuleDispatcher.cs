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

    public async Task<ModuleExecutionResult> BuildSystemContextAsync(Guid workspaceId, IntentResultDto intentResult, CancellationToken cancellationToken)
    {
        var customerPhone = intentResult.Parameters.ContainsKey("phone") ? intentResult.Parameters["phone"]?.ToString() ?? "unknown" : "unknown";
        var context = await _conversationCache.GetContextAsync(workspaceId, customerPhone, cancellationToken) ?? new ConversationContextDto();

        if (!string.IsNullOrEmpty(context.PendingAction) && intentResult.Intent == IntentType.Unknown)
        {
            if (Enum.TryParse<IntentType>(context.CurrentIntent, true, out var previousIntent))
            {
                intentResult = new IntentResultDto(previousIntent, 1.0, intentResult.Parameters);
            }
        }
        else if (intentResult.Intent != IntentType.Unknown)
        {
            context.CurrentIntent = intentResult.Intent.ToString();
            context.PendingAction = null;
        }

        if (intentResult.Parameters.TryGetValue("locationId", out var locId) && locId != null) context.SelectedLocationId = locId.ToString();
        if (intentResult.Parameters.TryGetValue("serviceId", out var srvId) && srvId != null) context.SelectedServiceId = srvId.ToString();
        if (intentResult.Parameters.TryGetValue("date", out var date) && date != null) context.PendingDate = date.ToString();
        if (intentResult.Parameters.TryGetValue("time", out var time) && time != null) context.PendingTime = time.ToString();

        // Inyectamos la memoria guardada en los parámetros de este turno
        if (!string.IsNullOrEmpty(context.SelectedLocationId) && !intentResult.Parameters.ContainsKey("locationId"))
            intentResult.Parameters["locationId"] = context.SelectedLocationId;
        if (!string.IsNullOrEmpty(context.SelectedServiceId) && !intentResult.Parameters.ContainsKey("serviceId"))
            intentResult.Parameters["serviceId"] = context.SelectedServiceId;

        // 🔥 SPRINT 3 (Auditoría #14): Restaurar PendingDate y PendingTime para que la IA no pierda la fecha
        if (!string.IsNullOrEmpty(context.PendingDate) && !intentResult.Parameters.ContainsKey("date"))
            intentResult.Parameters["date"] = context.PendingDate;
        if (!string.IsNullOrEmpty(context.PendingTime) && !intentResult.Parameters.ContainsKey("time"))
            intentResult.Parameters["time"] = context.PendingTime;

        await _conversationCache.SetContextAsync(workspaceId, customerPhone, context, cancellationToken);

        var capabilityRequest = MapIntentToCapability(intentResult);
        if (capabilityRequest == null)
            return new ModuleExecutionResult(false, "SYSTEM", "UNKNOWN", "Responde cortésmente que no lograste entender la solicitud o pregúntale al cliente si desea que lo derivemos con un agente humano.", true);

        bool hasAccess = await _entitlementService.HasCapabilityAccessAsync(workspaceId, capabilityRequest.ModuleCode, capabilityRequest.CapabilityCode, cancellationToken);
        if (!hasAccess)
            return new ModuleExecutionResult(false, capabilityRequest.ModuleCode, capabilityRequest.CapabilityCode, $"El negocio no tiene contratado el módulo de {capabilityRequest.ModuleCode}. Discúlpate amablemente y ofrécele ayuda con las opciones disponibles.");

        var handler = _moduleHandlers.FirstOrDefault(h => h.ModuleCode == capabilityRequest.ModuleCode);
        if (handler == null || !handler.SupportedCapabilities.Contains(capabilityRequest.CapabilityCode))
            return new ModuleExecutionResult(false, capabilityRequest.ModuleCode, capabilityRequest.CapabilityCode, $"Error interno. El módulo no está configurado.");

        var resultData = await handler.ExecuteCapabilityAsync(workspaceId, capabilityRequest, cancellationToken);

        // 🔥 SPRINT 3: Handoff Inteligente. Si el handler devuelve la etiqueta, activamos el flag y limpiamos el texto.
        bool requiresHuman = resultData.Contains("[RequiresHuman]", StringComparison.OrdinalIgnoreCase);
        if (requiresHuman)
        {
            resultData = resultData.Replace("[RequiresHuman]", "", StringComparison.OrdinalIgnoreCase).Trim();
        }

        var missingParams = new List<string>();
        if (resultData.Contains("locationId", StringComparison.OrdinalIgnoreCase) || resultData.Contains("sede", StringComparison.OrdinalIgnoreCase)) missingParams.Add("locationId");
        if (resultData.Contains("serviceId", StringComparison.OrdinalIgnoreCase) || resultData.Contains("servicio", StringComparison.OrdinalIgnoreCase)) missingParams.Add("serviceId");

        return new ModuleExecutionResult(true, capabilityRequest.ModuleCode, capabilityRequest.CapabilityCode, resultData, requiresHuman, missingParams.ToArray());
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
            IntentType.GeneralGreeting => new CapabilityRequest("BUSINESS_PROFILE", "READ", intentResult.Parameters),
            IntentType.HumanHandoffRequest => new CapabilityRequest("CONVERSATIONS", "TAKEOVER", intentResult.Parameters),
            _ => null
        };
    }
}