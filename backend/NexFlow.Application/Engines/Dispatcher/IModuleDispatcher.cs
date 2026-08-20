using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Dispatcher;

public interface IModuleDispatcher
{
    // Construye el contexto crudo consultando las bases de datos correspondientes según la intención
    Task<string> BuildSystemContextAsync(Guid workspaceId, IntentResultDto intentResult, CancellationToken cancellationToken);
}