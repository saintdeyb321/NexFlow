using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Application.Engines.AI;

public interface IAiRouter
{
    Task<string> GenerateResponseAsync(
        Guid workspaceId,
        ModuleExecutionResult systemContext,
        CancellationToken cancellationToken);
}