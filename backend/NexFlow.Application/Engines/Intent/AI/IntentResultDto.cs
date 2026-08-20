using System.Collections.Generic;

namespace NexFlow.Application.Engines.Intent.AI;

public record IntentResultDto(
    IntentType Intent,
    double Confidence,
    Dictionary<string, string> Parameters
)
{
    // Threshold de 80% de confianza
    public bool IsConfident(double threshold = 0.80) => Confidence >= threshold;
}