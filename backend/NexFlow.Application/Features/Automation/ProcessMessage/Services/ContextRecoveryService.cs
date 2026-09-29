using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Enums;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IContextRecoveryService
{
    Task<ConversationContextDto> GetOrRecoverContextAsync(Guid workspaceId, string phone, CancellationToken ct);
}

public class ContextRecoveryService : IContextRecoveryService
{
    private readonly IConversationCache _cache;
    private readonly IConversationStateRepository _stateRepo; // 🔥 SPRINT 04: Nueva fuente de verdad

    public ContextRecoveryService(IConversationCache cache, IConversationStateRepository stateRepo)
    {
        _cache = cache;
        _stateRepo = stateRepo;
    }

    public async Task<ConversationContextDto> GetOrRecoverContextAsync(Guid workspaceId, string phone, CancellationToken ct)
    {
        // 1. Intentamos leer de Redis (Ruta feliz y ultra-rápida)
        var context = await _cache.GetContextAsync(workspaceId, phone, ct);
        if (context != null) return context;

        // 🔥 SPRINT 04: 2. Recuperación Segura y Durable desde Firestore (Cero conjeturas de texto)
        context = await _stateRepo.GetStateAsync(workspaceId, phone, ct);

        if (context == null)
        {
            context = new ConversationContextDto(); // Estado completamente limpio
        }

        // 3. Volvemos a guardar en Redis para el siguiente turno
        await _cache.SetContextAsync(workspaceId, phone, context, ct);

        return context;
    }
}