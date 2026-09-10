namespace NexFlow.Application.Abstractions.Integrations;

public interface IMessageGateway
{
    Task<string> SendTextAsync(Guid workspaceId, string customerIdentifier, string message, CancellationToken cancellationToken);
    Task<string> SendDocumentAsync(Guid workspaceId, string customerIdentifier, string documentUrl, string fileName, string caption, CancellationToken cancellationToken);
}