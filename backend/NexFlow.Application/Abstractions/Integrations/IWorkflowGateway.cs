using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Abstractions.Integrations;

// V2.12: Contrato estricto para n8n
public record N8nEventPayload<T>(
    Guid WorkspaceId,
    string EventType,      // Ej: "RESERVATION_CREATED" o "NEW_CUSTOMER"
    string CorrelationId,  // Para buscar errores en logs de Serilog
    string IdempotencyKey, // Evitar que n8n dispare el mismo evento dos veces
    DateTime Timestamp,
    T Data                 // Los datos reales (Ej: ReservationDto)
);

public interface IWorkflowGateway
{
    Task TriggerWorkflowAsync<T>(string workflowId, N8nEventPayload<T> payload, CancellationToken cancellationToken);
}