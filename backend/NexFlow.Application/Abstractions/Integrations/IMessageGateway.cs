namespace NexFlow.Application.Abstractions.Integrations;

public interface IMessageGateway
{
    // Agregamos string messageId
    Task<string> SendTextAsync(Guid workspaceId, string customerIdentifier, string message, string messageId, CancellationToken cancellationToken);
    Task<string> SendDocumentAsync(Guid workspaceId, string customerIdentifier, string documentUrl, string fileName, string caption, string messageId, CancellationToken cancellationToken);

    // Si tenías SendImageAsync en la interfaz, agrégalo también:
    Task<string> SendImageAsync(Guid workspaceId, string customerIdentifier, string imageUrl, string caption, string messageId, CancellationToken cancellationToken);
}