using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Application.Engines.Dispatcher;

public interface IModuleHandler
{
    string ModuleCode { get; }
    string[] SupportedCapabilities { get; }
    Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken);
}