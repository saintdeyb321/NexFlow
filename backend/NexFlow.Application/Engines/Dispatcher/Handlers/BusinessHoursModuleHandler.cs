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
        var hours = await _hoursRepo.GetBusinessHoursAsync(workspaceId, null, cancellationToken);
        if (hours == null)
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "Los horarios no están configurados.", false, Array.Empty<string>());

        var data = JsonSerializer.Serialize(hours);
        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, data, false, Array.Empty<string>());
    }
}