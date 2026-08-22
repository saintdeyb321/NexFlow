namespace NexFlow.Application.Engines.Dispatcher;

public interface IModuleHandler
{
    string ModuleCode { get; }

    // V2.15: Declaración explícita de las capacidades que el módulo exporta
    string[] SupportedCapabilities { get; }

    // V2.15: Recibe una Capacidad Estructurada, NO un Intent
    Task<string> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken);
}