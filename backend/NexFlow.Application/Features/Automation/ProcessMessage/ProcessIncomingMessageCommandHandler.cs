using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Common;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

public class ProcessIncomingMessageCommandHandler
{
    public ProcessIncomingMessageCommandHandler()
    {
        // En los siguientes sprints inyectaremos aquí el IntentEngine, IAiRouter, etc.
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // TODO: Implementar la orquestación real del mensaje
        // 1. Clasificar intención (IntentEngine)
        // 2. Ejecutar acción (Ej. ReservationEngine)
        // 3. Responder al cliente (IMessageGateway)

        await Task.CompletedTask;
        return Result.Success();
    }
}