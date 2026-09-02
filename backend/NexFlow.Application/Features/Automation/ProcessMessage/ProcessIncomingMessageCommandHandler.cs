using NexFlow.Application.Common;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;

namespace NexFlow.Application.Features.Automation.ProcessMessage;

public class ProcessIncomingMessageCommandHandler
{
    private readonly IIncomingMessageGuard _guard;
    private readonly IConversationStateService _stateService;
    private readonly IAiResponseOrchestrator _aiOrchestrator;

    public ProcessIncomingMessageCommandHandler(
        IIncomingMessageGuard guard,
        IConversationStateService stateService,
        IAiResponseOrchestrator aiOrchestrator)
    {
        _guard = guard;
        _stateService = stateService;
        _aiOrchestrator = aiOrchestrator;
    }

    public async Task<Result> Handle(ProcessIncomingMessageCommand request, CancellationToken cancellationToken)
    {
        // 1. Filtrar, Validar Idempotencia y Licencia
        var guardResult = await _guard.CheckMessageAsync(request, cancellationToken);
        if (!guardResult.IsValid) return Result.Success();

        // 2. Gestionar Estado (Intervención humana, guardar consumidor y mensaje)
        var stateResult = await _stateService.ProcessStateAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, cancellationToken);
        if (!stateResult.ShouldAiRespond) return Result.Success();

        // 3. Orquestar Memoria, Inteligencia Artificial y Envío de WhatsApp
        await _aiOrchestrator.RespondAsync(guardResult.WorkspaceId, guardResult.NormalizedPhone, request, stateResult.Record, cancellationToken);

        return Result.Success();
    }
}