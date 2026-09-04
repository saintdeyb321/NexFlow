#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Engines.Intent.AI;

namespace NexFlow.Application.Engines.Dispatcher;

public class ModuleDispatcher : IModuleDispatcher
{
    private readonly ICapabilityResolver _capabilityResolver;
    private readonly IContextResolver _contextResolver;
    private readonly IModuleAuthorizer _moduleAuthorizer;
    private readonly IModuleExecutor _moduleExecutor;

    public ModuleDispatcher(
        ICapabilityResolver capabilityResolver,
        IContextResolver contextResolver,
        IModuleAuthorizer moduleAuthorizer,
        IModuleExecutor moduleExecutor)
    {
        _capabilityResolver = capabilityResolver;
        _contextResolver = contextResolver;
        _moduleAuthorizer = moduleAuthorizer;
        _moduleExecutor = moduleExecutor;
    }

    public async Task<ModuleExecutionResult> BuildSystemContextAsync(Guid workspaceId, string customerPhone, IntentResultDto intentResult, CancellationToken cancellationToken)
    {
        // 1. Traducir Intención
        var capabilityRequest = _capabilityResolver.Resolve(intentResult);

        // 2. Gestionar Contexto y Reglas de Sede
        var resolution = await _contextResolver.EvaluateContextAsync(workspaceId, customerPhone, intentResult, capabilityRequest, cancellationToken);

        // Si el ContextResolver decide interceptar (Ej. falta sede, error, o saludo) lo devolvemos directo.
        if (resolution.InterceptResult != null)
        {
            await _contextResolver.SaveContextAsync(workspaceId, customerPhone, resolution.Context, cancellationToken);
            return resolution.InterceptResult;
        }

        // 3. Validar Licencia
        bool hasAccess = await _moduleAuthorizer.IsAuthorizedAsync(workspaceId, capabilityRequest!.ModuleCode, capabilityRequest.CapabilityCode, cancellationToken);
        if (!hasAccess)
        {
            return new ModuleExecutionResult(false, capabilityRequest.ModuleCode, capabilityRequest.CapabilityCode, $"El negocio no tiene contratado el módulo de {capabilityRequest.ModuleCode}.");
        }

        // 4. Ejecutar Módulo
        var executionResult = await _moduleExecutor.ExecuteAsync(workspaceId, capabilityRequest, cancellationToken);

        // Actualizar acción pendiente si faltaron parámetros en el Handler final
        if (executionResult.MissingParameters != null && executionResult.MissingParameters.Any())
            resolution.Context.PendingAction = $"ASK_{executionResult.MissingParameters.First().ToUpperInvariant()}";
        else
            resolution.Context.PendingAction = null;

        await _contextResolver.SaveContextAsync(workspaceId, customerPhone, resolution.Context, cancellationToken);

        return executionResult;
    }
}