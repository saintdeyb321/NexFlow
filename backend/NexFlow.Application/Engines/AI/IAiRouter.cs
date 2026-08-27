using NexFlow.Application.Engines.Dispatcher;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.AI;

public interface IAiRouter
{
    Task<string> GenerateResponseAsync(
        Guid workspaceId, 
        IntentResultDto intent, 
        ModuleExecutionResult systemContext, 
        CancellationToken cancellationToken);
}