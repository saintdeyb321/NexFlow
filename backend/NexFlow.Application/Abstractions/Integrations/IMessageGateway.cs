namespace NexFlow.Application.Abstractions.Integrations;

public interface IMessageGateway
{
    // Abstracción para enviar mensajes (Evolution API / Meta Cloud API)
    Task SendTextAsync(Guid workspaceId, string customerIdentifier, string message, CancellationToken cancellationToken);
}