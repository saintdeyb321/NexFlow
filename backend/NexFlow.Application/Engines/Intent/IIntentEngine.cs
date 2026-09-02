using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Intent;

public interface IIntentEngine
{
    Task<IntentResultDto> AnalyzeAsync(string message, ConversationContextDto? context, CancellationToken cancellationToken);
}