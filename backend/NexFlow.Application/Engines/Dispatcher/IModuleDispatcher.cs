using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Dispatcher;

public record ModuleExecutionResult(
    bool Success,
    string ModuleCode,
    string Capability,
    string Data,
    bool RequiresHuman = false,
    string[]? MissingParameters = null
);

public interface IModuleDispatcher
{
    Task<ModuleExecutionResult> BuildSystemContextAsync(Guid workspaceId, string customerPhone, IntentResultDto intentResult, CancellationToken cancellationToken);
}