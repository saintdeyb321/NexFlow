using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Reservations;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class ReservationModuleHandler : IModuleHandler
{
    public string ModuleCode => "RESERVATIONS";

    private readonly IReservationEngine _reservationEngine;
    private readonly IConversationCache _conversationCache;

    public ReservationModuleHandler(
        IReservationEngine reservationEngine,
        IConversationCache conversationCache)
    {
        _reservationEngine = reservationEngine;
        _conversationCache = conversationCache;
    }

    public string[] SupportedCapabilities => new[] { "CHECK_AVAILABILITY", "CREATE", "CANCEL" };

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        var phone = request.Parameters.TryGetValue("phone", out var p) ? p?.ToString() ?? "unknown" : "unknown";
        var context = await _conversationCache.GetContextAsync(workspaceId, phone, cancellationToken) ?? new ConversationContextDto();

        if (request.CapabilityCode == "CANCEL")
        {
            context.SelectedLocationId = null; context.SelectedServiceId = null; context.PendingAction = null; context.CurrentIntent = null;
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);

            var cancelResult = await _reservationEngine.CancelActiveReservationAsync(workspaceId, phone, cancellationToken);

            if (cancelResult.IsSuccess)
            {
                var cancelledRes = cancelResult.Value;
                var successData = new { status = "cancelled", reservationId = cancelledRes.Id, date = cancelledRes.StartTime.ToString("yyyy-MM-dd"), time = cancelledRes.StartTime.ToString("HH:mm") };
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(successData), false, Array.Empty<string>());
            }
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "NOT_FOUND", message = "No tienes ninguna reserva activa para cancelar en este momento." }), false, Array.Empty<string>());
        }

        // 🔥 LA MÁQUINA DE ESTADOS
        var currentState = Enum.TryParse<ReservationConversationState>(context.PendingAction, true, out var parsedState)
            ? parsedState
            : ReservationConversationState.SelectingLocation;

        // TRANSICIÓN 1: SEDE
        string? targetLocationId = request.Parameters.TryGetValue("locationId", out var locId) ? locId?.ToString() : context.SelectedLocationId;
        if (string.IsNullOrEmpty(targetLocationId))
        {
            return await StepBackAndRequestParameter(workspaceId, phone, context, ReservationConversationState.SelectingLocation, "location", "locationId", request.CapabilityCode, cancellationToken);
        }
        context.SelectedLocationId = targetLocationId; // Aseguramos el estado
        currentState = ReservationConversationState.SelectingService; // Avanzamos

        // TRANSICIÓN 2: SERVICIO
        string? targetServiceId = request.Parameters.TryGetValue("serviceId", out var srvId) ? srvId?.ToString() : context.SelectedServiceId;
        if (string.IsNullOrEmpty(targetServiceId))
        {
            return await StepBackAndRequestParameter(workspaceId, phone, context, currentState, "service", "serviceId", request.CapabilityCode, cancellationToken);
        }
        context.SelectedServiceId = targetServiceId;
        currentState = ReservationConversationState.SelectingDate;

        // TRANSICIÓN 3: FECHA
        DateTime dateToSearch;
        if (request.Parameters.TryGetValue("date", out var dateStr) && dateStr != null && DateTime.TryParse(dateStr.ToString(), out var parsedDate))
        {
            if (parsedDate.Date < DateTime.UtcNow.AddHours(-5).Date)
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "invalid_date", message = "No se puede reservar en fechas pasadas" }), false, new[] { "date" });
            dateToSearch = parsedDate.Date;
        }
        else
        {
            return await StepBackAndRequestParameter(workspaceId, phone, context, currentState, "date", "date", request.CapabilityCode, cancellationToken);
        }
        currentState = ReservationConversationState.SelectingTime;

        // FLUJO DIVIDIDO: CHECK_AVAILABILITY O CREATE
        if (request.CapabilityCode == "CHECK_AVAILABILITY")
        {
            context.PendingAction = currentState.ToString();
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);

            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, targetLocationId, targetServiceId, dateToSearch, cancellationToken);
            if (!slots.Any())
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "no_availability", date = dateToSearch.ToString("yyyy-MM-dd") }), false, Array.Empty<string>());

            var availableTimes = slots.Select(s => s.StartTime.ToString("HH:mm")).ToList();
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "available", date = dateToSearch.ToString("yyyy-MM-dd"), times = availableTimes }), false, new[] { "time" });
        }

        if (request.CapabilityCode == "CREATE")
        {
            // TRANSICIÓN 4: HORA
            if (!request.Parameters.TryGetValue("time", out var timeStr) || timeStr == null || !TimeSpan.TryParse(timeStr.ToString(), out var time))
            {
                return await StepBackAndRequestParameter(workspaceId, phone, context, currentState, "time", "time", request.CapabilityCode, cancellationToken);
            }
            currentState = ReservationConversationState.Confirming;

            // TRANSICIÓN 5: NOMBRE / CONFIRMACIÓN
            if (!request.Parameters.TryGetValue("name", out var customerName) || customerName == null || string.IsNullOrWhiteSpace(customerName.ToString()))
            {
                return await StepBackAndRequestParameter(workspaceId, phone, context, currentState, "name", "name", request.CapabilityCode, cancellationToken);
            }

            // EJECUCIÓN FINAL
            var exactDateTime = dateToSearch.Add(time);
            var result = await _reservationEngine.CreateReservationAsync(workspaceId, targetLocationId, targetServiceId, phone, customerName.ToString()!, exactDateTime, cancellationToken);

            if (result.IsSuccess)
            {
                context.SelectedLocationId = null; context.SelectedServiceId = null; context.PendingAction = null; context.CurrentIntent = null;
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "created", date = exactDateTime.ToString("yyyy-MM-dd HH:mm"), name = customerName.ToString() }), false, Array.Empty<string>());
            }
            else
            {
                return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "conflict", reason = result.Error.Description }), false, new[] { "time" });
            }
        }

        return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { error = "Intención no soportada" }), false, Array.Empty<string>());
    }

    private async Task<ModuleExecutionResult> StepBackAndRequestParameter(Guid workspaceId, string phone, ConversationContextDto context, ReservationConversationState state, string paramName, string paramCode, string capabilityCode, CancellationToken ct)
    {
        context.PendingAction = state.ToString();
        await _conversationCache.SetContextAsync(workspaceId, phone, context, ct);
        return new ModuleExecutionResult(true, ModuleCode, capabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = paramName }), false, new[] { paramCode });
    }
}