using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Dispatcher;

public class ModuleDispatcher : IModuleDispatcher
{
    private readonly IEnumerable<IModuleHandler> _moduleHandlers;
    private readonly IEntitlementService _entitlementService;

    public ModuleDispatcher(
        IEnumerable<IModuleHandler> moduleHandlers,
        IEntitlementService entitlementService)
    {
        _moduleHandlers = moduleHandlers;
        _entitlementService = entitlementService;
    }

    public async Task<string> BuildSystemContextAsync(Guid workspaceId, IntentResultDto intentResult, CancellationToken cancellationToken)
    {
        var handler = _moduleHandlers.FirstOrDefault(h => h.CanHandle(intentResult.Intent));
        if (handler == null) return "El sistema no comprende esta solicitud de intención.";

        var availableModules = await _entitlementService.GetAvailableModuleCodesAsync(workspaceId, cancellationToken);
        if (!availableModules.Contains(handler.ModuleCode))
        {
            return $"El negocio no tiene contratado el módulo de {handler.ModuleCode}. Responde cortésmente que esta función no está disponible por el momento.";
        }

        return await handler.ExecuteCapabilityAsync(workspaceId, intentResult, cancellationToken);
    }
}