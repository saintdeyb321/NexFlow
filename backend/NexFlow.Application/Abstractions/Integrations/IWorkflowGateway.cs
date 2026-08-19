namespace NexFlow.Application.Abstractions.Integrations;

public interface IWorkflowGateway
{
    // Abstracción para disparar flujos en n8n
    Task TriggerWorkflowAsync(Guid workspaceId, string workflowId, object payload, CancellationToken cancellationToken);
}