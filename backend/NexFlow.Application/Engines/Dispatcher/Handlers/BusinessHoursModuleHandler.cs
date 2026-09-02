using System.Text;
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
        // 🔥 Auditoría (Sprint 3.1): Leer la sede seleccionada por el Orquestador/Usuario
        string? locationId = null;
        if (request.Parameters.TryGetValue("locationId", out var locObj) && locObj != null)
        {
            locationId = locObj.ToString();
        }

        // Le pasamos el locationId al repositorio en lugar del null que tenías antes
        var hours = await _hoursRepo.GetBusinessHoursAsync(workspaceId, locationId, cancellationToken);

        if (hours == null || !hours.Any())
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "Los horarios comerciales aún no han sido configurados para esta sede.", false, Array.Empty<string>());

        // 🔥 Convertimos la data cruda a un texto legible y determinista para evitar alucinaciones
        var sb = new StringBuilder();
        sb.AppendLine("Estos son los horarios de atención de la sede:");

        foreach (var day in hours.OrderBy(h => h.DayOfWeek))
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

            if (!day.IsClosed)
            {
                sb.AppendLine($"- {dayName}: Cerrado");
            }
            else
            {
                sb.AppendLine($"- {dayName}: {day.OpenTime} a {day.CloseTime}");
            }
        }

        var responseText = $"Responde la consulta del cliente utilizando estrictamente los siguientes horarios. Si el día está marcado como 'Cerrado', indícalo amablemente.\n\n{sb}";

        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, responseText, false, Array.Empty<string>());
    }
}