namespace NexFlow.Application.Engines.Dispatcher;

public interface IModuleHandler
{
    string ModuleCode { get; }
    string[] SupportedCapabilities { get; }
    Task<string> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken);
}