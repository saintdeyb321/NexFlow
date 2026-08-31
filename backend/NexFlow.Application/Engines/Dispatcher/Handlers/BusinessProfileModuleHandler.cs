using System.Text.Json;
using NexFlow.Application.Abstractions;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class BusinessProfileModuleHandler : IModuleHandler
{
    private readonly IBusinessProfileRepository _profileRepo;

    public BusinessProfileModuleHandler(IBusinessProfileRepository profileRepo) => _profileRepo = profileRepo;

    public string ModuleCode => "BUSINESS_PROFILE";
    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        var profile = await _profileRepo.GetProfileAsync(workspaceId, cancellationToken);
        if (profile == null)
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "El perfil del negocio no está configurado.", false, Array.Empty<string>());

        var data = JsonSerializer.Serialize(profile);
        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, data, false, Array.Empty<string>());
    }
}