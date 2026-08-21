using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Dispatcher;

public interface IModuleHandler
{
    string ModuleCode { get; }
    bool CanHandle(IntentType intent);
    Task<string> ExecuteCapabilityAsync(Guid workspaceId, IntentResultDto intentResult, CancellationToken cancellationToken);
}