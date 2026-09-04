using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Reservations;

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
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "cancelled_request", message = "Solicitud recibida. Un asesor lo contactará." }), true);
        }

        // --- PASO 1 y 2: Validar Sede y Servicio ---
        string? targetLocationId = request.Parameters.TryGetValue("locationId", out var locId) ? locId?.ToString() : context.SelectedLocationId;
        string? targetServiceId = request.Parameters.TryGetValue("serviceId", out var srvId) ? srvId?.ToString() : context.SelectedServiceId;

        if (string.IsNullOrEmpty(targetLocationId))
        {
            context.PendingAction = "ASK_LOCATION";
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = "location", instruction = "Pídele amablemente al cliente que indique en cuál de las sedes desea realizar su reserva." }), false, new[] { "locationId" });
        }

        if (string.IsNullOrEmpty(targetServiceId))
        {
            context.PendingAction = "ASK_SERVICE";
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = "service", instruction = "Pregúntale amablemente al cliente qué servicio específico desea reservar." }), false, new[] { "serviceId" });
        }
        // --- PASO 3: Validar Fecha ---
        DateTime dateToSearch;
        if (request.Parameters.TryGetValue("date", out var dateStr) && dateStr != null && DateTime.TryParse(dateStr.ToString(), out var parsedDate))
        {
            // 🔥 Auditoría (Sprint 4.1): No permitir reservas en el pasado
            if (parsedDate.Date < DateTime.UtcNow.AddHours(-5).Date) // Aproximación Zona Horaria
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "invalid_date", message = "No se puede reservar en fechas pasadas" }), false, new[] { "date" });

            dateToSearch = parsedDate.Date;
        }
        else
        {
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = "date" }), false, new[] { "date" });
        }

        if (request.CapabilityCode == "CHECK_AVAILABILITY")
        {
            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, targetLocationId, targetServiceId, dateToSearch, cancellationToken);

            if (!slots.Any())
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "no_availability", date = dateToSearch.ToString("yyyy-MM-dd") }));

            var availableTimes = slots.Select(s => s.StartTime.ToString("HH:mm")).ToList();
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "available", date = dateToSearch.ToString("yyyy-MM-dd"), times = availableTimes }), false, new[] { "time" });
        }

        if (request.CapabilityCode == "CREATE")
        {
            // --- PASO 4: Validar Hora ---
            if (!request.Parameters.TryGetValue("time", out var timeStr) || timeStr == null || !TimeSpan.TryParse(timeStr.ToString(), out var time))
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = "time" }), false, new[] { "time" });

            // --- PASO 5: FORZAR CHECK AVAILABILITY ANTES DE PEDIR NOMBRE ---
            // Esto evita crear la reserva si la hora seleccionada acaba de ser ocupada o no existía.
            var slots = await _reservationEngine.GetAvailabilityAsync(workspaceId, targetLocationId, targetServiceId, dateToSearch, cancellationToken);
            var isTimeSlotValid = slots.Any(s => s.StartTime.TimeOfDay == time);

            if (!isTimeSlotValid)
            {
                // Si la hora es inválida o se acaba de ocupar, retrocedemos al paso de pedir hora.
                context.PendingAction = "ASK_TIME";
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);

                var availableTimes = slots.Select(s => s.StartTime.ToString("HH:mm")).ToList();
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new
                {
                    status = "time_unavailable_or_invalid",
                    date = dateToSearch.ToString("yyyy-MM-dd"),
                    requestedTime = timeStr.ToString(),
                    availableAlternatives = availableTimes
                }), false, new[] { "time" });
            }

            // --- PASO 6: Pedir Nombre (Último paso) ---
            if (!request.Parameters.TryGetValue("name", out var customerName) || customerName == null || string.IsNullOrWhiteSpace(customerName.ToString()))
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = "name" }), false, new[] { "name" });

            // --- EJECUCIÓN DEL CREATE ---
            var exactDateTime = dateToSearch.Add(time);
            var result = await _reservationEngine.CreateReservationAsync(workspaceId, targetLocationId, targetServiceId, phone, customerName.ToString()!, exactDateTime, cancellationToken);

            if (result.IsSuccess)
            {
                context.SelectedLocationId = null; context.SelectedServiceId = null; context.PendingAction = null; context.CurrentIntent = null;
                await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "created", date = exactDateTime.ToString("yyyy-MM-dd HH:mm"), name = customerName.ToString() }));
            }
            else
            {
                return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "conflict", reason = result.Error.Description }));
            }
        }

        return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { error = "Intención no soportada" }));
    }
}