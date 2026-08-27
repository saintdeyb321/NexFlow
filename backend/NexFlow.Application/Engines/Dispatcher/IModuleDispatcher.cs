using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Dispatcher;

// 🔥 SPRINT 7: El nuevo contrato estructurado que aniquila las alucinaciones de la IA
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
    Task<ModuleExecutionResult> BuildSystemContextAsync(Guid workspaceId, IntentResultDto intentResult, CancellationToken cancellationToken);
}