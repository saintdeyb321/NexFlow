using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class BusinessHoursModuleHandler : IModuleHandler
{
    private readonly IBusinessHoursRepository _hoursRepo;

    public BusinessHoursModuleHandler(IBusinessHoursRepository hoursRepo) => _hoursRepo = hoursRepo;

    public string ModuleCode => "BUSINESS_HOURS";
    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        string? locationId = null;
        if (request.Parameters.TryGetValue("locationId", out var locObj) && locObj != null)
        {
            locationId = locObj.ToString();
        }

        var hours = await _hoursRepo.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);

        if (hours == null || !hours.Any())
        {
            // SPRINT 9: Error estandarizado en JSON
            var errorData = JsonSerializer.Serialize(new { status = "NOT_FOUND", message = "Horarios no configurados." });
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, errorData, false, Array.Empty<string>());
        }

        // 🔥 SPRINT 1 (P0): Lógica corregida.
        // 🔥 SPRINT 9 y 10 (P0): Eliminamos el StringBuilder/Prompt. Creamos una estructura de datos real.
        var scheduleList = hours.OrderBy(h => h.DayOfWeek).Select(day =>
        {
            var dayName = day.DayOfWeek switch
            {
                1 => "Lunes",
                2 => "Martes",
                3 => "Miércoles",
                4 => "Jueves",
                5 => "Viernes",
                6 => "Sábado",
                0 => "Domingo",
                _ => "Día Desconocido"
            };

            return new
            {
                day = dayName,
                isClosed = day.IsClosed,
                // Lógica corregida: Si está cerrado, decimos cerrado. Si no, damos el rango.
                schedule = day.IsClosed ? "Cerrado" : $"{day.OpenTime} a {day.CloseTime}"
            };
        });

        // SPRINT 9: El contrato de salida ahora es JSON puro, no texto ni instrucciones.
        var responseData = JsonSerializer.Serialize(new
        {
            status = "SUCCESS",
            data = scheduleList
        });

        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, responseData, false, Array.Empty<string>());
    }
}