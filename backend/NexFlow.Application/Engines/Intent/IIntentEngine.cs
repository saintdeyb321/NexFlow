using NexFlow.Application.DTOs.AI;

namespace NexFlow.Application.Engines.Intent;

public interface IIntentEngine
{
    // Transforma lenguaje natural en una intención estructurada
    Task<IntentResultDto> AnalyzeAsync(string message, CancellationToken cancellationToken);
}