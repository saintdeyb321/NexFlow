using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Dispatcher;

public class ModuleDispatcher : IModuleDispatcher
{
    private readonly IEnumerable<IModuleHandler> _moduleHandlers;
    private readonly IEntitlementService _entitlementService;
    private readonly IConversationCache _conversationCache;
    private readonly ILocationRepository _locationRepository;

    public ModuleDispatcher(
        IEnumerable<IModuleHandler> moduleHandlers,
        IEntitlementService entitlementService,
        IConversationCache conversationCache,
        ILocationRepository locationRepository)
    {
        _moduleHandlers = moduleHandlers;
        _entitlementService = entitlementService;
        _conversationCache = conversationCache;
        _locationRepository = locationRepository;
    }

    public async Task<ModuleExecutionResult> BuildSystemContextAsync(Guid workspaceId, IntentResultDto intentResult, CancellationToken cancellationToken)
    {
        var customerPhone = intentResult.Parameters.ContainsKey("phone") ? intentResult.Parameters["phone"]?.ToString() ?? "unknown" : "unknown";

        var context = await _conversationCache.GetContextAsync(workspaceId, customerPhone, cancellationToken) ?? new ConversationContextDto();

        if (!string.IsNullOrEmpty(context.PendingAction))
        {
            bool isInterrupt = intentResult.Intent == IntentType.CancelReservation || intentResult.Intent == IntentType.HumanHandoffRequest;

            if (!isInterrupt && Enum.TryParse<IntentType>(context.CurrentIntent, true, out var previousIntent))
            {
                intentResult = new IntentResultDto(previousIntent, 1.0, intentResult.Parameters);
            }
            else if (isInterrupt)
            {
                context.PendingAction = null;
                context.CurrentIntent = intentResult.Intent.ToString();
            }
        }
        else if (intentResult.Intent != IntentType.Unknown)
        {
            context.CurrentIntent = intentResult.Intent.ToString();
        }

        if (intentResult.Parameters.TryGetValue("locationId", out var locId) && locId != null) context.SelectedLocationId = locId.ToString();
        if (intentResult.Parameters.TryGetValue("serviceId", out var srvId) && srvId != null) context.SelectedServiceId = srvId.ToString();
        if (intentResult.Parameters.TryGetValue("date", out var date) && date != null) context.PendingDate = date.ToString();
        if (intentResult.Parameters.TryGetValue("time", out var time) && time != null) context.PendingTime = time.ToString();

        if (!string.IsNullOrEmpty(context.SelectedLocationId) && !intentResult.Parameters.ContainsKey("locationId"))
            intentResult.Parameters["locationId"] = context.SelectedLocationId;
        if (!string.IsNullOrEmpty(context.SelectedServiceId) && !intentResult.Parameters.ContainsKey("serviceId"))
            intentResult.Parameters["serviceId"] = context.SelectedServiceId;
        if (!string.IsNullOrEmpty(context.PendingDate) && !intentResult.Parameters.ContainsKey("date"))
            intentResult.Parameters["date"] = context.PendingDate;
        if (!string.IsNullOrEmpty(context.PendingTime) && !intentResult.Parameters.ContainsKey("time"))
            intentResult.Parameters["time"] = context.PendingTime;

        var capabilityRequest = MapIntentToCapability(intentResult);
        if (capabilityRequest == null)
        {
            await _conversationCache.SetContextAsync(workspaceId, customerPhone, context, cancellationToken);
            // 🔥 CORRECCIÓN: Se actualiza el mensaje para que sea un texto amigable directo al usuario, y no bloquee el chat con un handoff innecesario.
            return new ModuleExecutionResult(false, "SYSTEM", "UNKNOWN", "Lo siento, no logré comprender tu consulta. ¿Podrías darme un poco más de detalle?", false, Array.Empty<string>());
        }

        if (capabilityRequest.ModuleCode == "CORE")
        {
            await _conversationCache.SetContextAsync(workspaceId, customerPhone, context, cancellationToken);

            if (capabilityRequest.CapabilityCode == "TAKEOVER")
                return new ModuleExecutionResult(true, "CORE", "TAKEOVER", "Un momento, te transferiré con un asesor humano.", true, Array.Empty<string>());

            if (capabilityRequest.CapabilityCode == "GREETING")
                return new ModuleExecutionResult(true, "CORE", "GREETING", "¡Hola! Soy el asistente virtual corporativo. ¿En qué te puedo ayudar?", false, Array.Empty<string>());
        }

        bool requiresLocation = capabilityRequest.ModuleCode is "RESERVATIONS" or "SERVICES" or "CATALOG" or "BUSINESS_HOURS";

        if (requiresLocation && string.IsNullOrEmpty(context.SelectedLocationId))
        {
            var locations = await _locationRepository.GetLocationsAsync(workspaceId, cancellationToken);
            var locationsList = locations.ToList();

            if (locationsList.Count > 1)
            {
                var compactLocations = locationsList.Select(l => new { id = l.Id, name = l.Name }).ToList();
                var dataStr = JsonSerializer.Serialize(compactLocations);

                context.PendingAction = "ASK_LOCATIONID";
                await _conversationCache.SetContextAsync(workspaceId, customerPhone, context, cancellationToken);

                return new ModuleExecutionResult(
                    Success: true,
                    ModuleCode: "SYSTEM",
                    Capability: "DISAMBIGUATE_LOCATION",
                    Data: $"El negocio tiene varias sedes. Basándote en estas opciones: {dataStr}, pregunta amablemente al usuario en cuál de ellas desea realizar la consulta.",
                    RequiresHuman: false,
                    MissingParameters: new[] { "locationId" }
                );
            }
            else if (locationsList.Count == 1)
            {
                context.SelectedLocationId = locationsList[0].Id;

                if (capabilityRequest.Parameters != null && context.SelectedLocationId != null)
                {
                    capabilityRequest.Parameters["locationId"] = context.SelectedLocationId;
                }
            }
        }

        bool hasAccess = await _entitlementService.HasCapabilityAccessAsync(workspaceId, capabilityRequest.ModuleCode, capabilityRequest.CapabilityCode, cancellationToken);
        if (!hasAccess)
            return new ModuleExecutionResult(false, capabilityRequest.ModuleCode, capabilityRequest.CapabilityCode, $"El negocio no tiene contratado el módulo de {capabilityRequest.ModuleCode}.", false, Array.Empty<string>());

        var handler = _moduleHandlers.FirstOrDefault(h => h.ModuleCode == capabilityRequest.ModuleCode);
        if (handler == null || !handler.SupportedCapabilities.Contains(capabilityRequest.CapabilityCode))
            return new ModuleExecutionResult(false, capabilityRequest.ModuleCode, capabilityRequest.CapabilityCode, $"Error interno. El módulo {capabilityRequest.ModuleCode} no está configurado.", false, Array.Empty<string>());

        var executionResult = await handler.ExecuteCapabilityAsync(workspaceId, capabilityRequest, cancellationToken);

        if (executionResult.MissingParameters != null && executionResult.MissingParameters.Any())
            context.PendingAction = $"ASK_{executionResult.MissingParameters.First().ToUpperInvariant()}";
        else
            context.PendingAction = null;

        await _conversationCache.SetContextAsync(workspaceId, customerPhone, context, cancellationToken);
        return executionResult;
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
            IntentType.LocationQuery => new CapabilityRequest("LOCATIONS", "READ", intentResult.Parameters),
            IntentType.BusinessHoursQuery => new CapabilityRequest("BUSINESS_HOURS", "READ", intentResult.Parameters),
            IntentType.BusinessProfileQuery => new CapabilityRequest("BUSINESS_PROFILE", "READ", intentResult.Parameters),
            IntentType.GeneralGreeting => new CapabilityRequest("CORE", "GREETING", intentResult.Parameters),
            IntentType.HumanHandoffRequest => new CapabilityRequest("CORE", "TAKEOVER", intentResult.Parameters),
            _ => null
        };
    }
}