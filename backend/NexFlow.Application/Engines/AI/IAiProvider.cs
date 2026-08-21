namespace NexFlow.Application.Engines.AI;

public interface IAiProvider
{
    Task<string> GenerateTextAsync(string systemPrompt, string userMessage, bool useJsonMode = false, CancellationToken cancellationToken = default);
}