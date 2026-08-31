namespace NexFlow.Application.Abstractions.Integrations;

public interface IMessageGateway
{
    Task<string> SendTextAsync(Guid workspaceId, string customerIdentifier, string message, CancellationToken cancellationToken);
}