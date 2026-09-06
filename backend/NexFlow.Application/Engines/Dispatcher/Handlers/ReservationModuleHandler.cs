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

        // 🔥 SPRINT 3 (P0): Cancelación Atómica y Real
        if (request.CapabilityCode == "CANCEL")
        {
            // Limpiamos el contexto en Redis inmediatamente
            context.SelectedLocationId = null; context.SelectedServiceId = null; context.PendingAction = null; context.CurrentIntent = null;
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);

            // Llamamos al motor para ejecutar la regla de dominio (reservation.Cancel()) y guardar en BD
            var cancelResult = await _reservationEngine.CancelActiveReservationAsync(workspaceId, phone, cancellationToken);

            if (cancelResult.IsSuccess)
            {
                var cancelledRes = cancelResult.Value; // Tu entidad Reservation devuelta por el Result<T>

                // Contrato estricto exigido por la auditoría
                var successData = new
                {
                    status = "cancelled",
                    reservationId = cancelledRes.Id,
                    date = cancelledRes.StartTime.ToString("yyyy-MM-dd"),
                    time = cancelledRes.StartTime.ToString("HH:mm")
                };

                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(successData), false, Array.Empty<string>());
            }
            else
            {
                var errorData = new { status = "NOT_FOUND", message = "No tienes ninguna reserva activa para cancelar en este momento." };
                return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(errorData), false, Array.Empty<string>());
            }
        }

        // --- PASO 1 y 2: Validar Sede y Servicio ---
        string? targetLocationId = request.Parameters.TryGetValue("locationId", out var locId) ? locId?.ToString() : context.SelectedLocationId;
        string? targetServiceId = request.Parameters.TryGetValue("serviceId", out var srvId) ? srvId?.ToString() : context.SelectedServiceId;

        if (string.IsNullOrEmpty(targetLocationId))
        {
            context.PendingAction = "ASK_LOCATION";
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = "location" }), false, new[] { "locationId" });
        }

        if (string.IsNullOrEmpty(targetServiceId))
        {
            context.PendingAction = "ASK_SERVICE";
            await _conversationCache.SetContextAsync(workspaceId, phone, context, cancellationToken);
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = "service" }), false, new[] { "serviceId" });
        }

        // --- PASO 3: Validar Fecha ---
        DateTime dateToSearch;
        if (request.Parameters.TryGetValue("date", out var dateStr) && dateStr != null && DateTime.TryParse(dateStr.ToString(), out var parsedDate))
        {
            if (parsedDate.Date < DateTime.UtcNow.AddHours(-5).Date)
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
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "no_availability", date = dateToSearch.ToString("yyyy-MM-dd") }), false, Array.Empty<string>());

            var availableTimes = slots.Select(s => s.StartTime.ToString("HH:mm")).ToList();
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "available", date = dateToSearch.ToString("yyyy-MM-dd"), times = availableTimes }), false, new[] { "time" });
        }

        if (request.CapabilityCode == "CREATE")
        {
            // --- PASO 4: Validar Hora ---
            if (!request.Parameters.TryGetValue("time", out var timeStr) || timeStr == null || !TimeSpan.TryParse(timeStr.ToString(), out var time))
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = "time" }), false, new[] { "time" });

            // --- PASO 5: Pedir Nombre ---
            if (!request.Parameters.TryGetValue("name", out var customerName) || customerName == null || string.IsNullOrWhiteSpace(customerName.ToString()))
            {
                // Limpiamos prompts también de las acciones pendientes
                return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "missing_parameter", parameter = "name" }), false, new[] { "name" });
            }

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
}