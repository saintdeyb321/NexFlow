using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public interface IContextRecoveryService
{
    Task<ConversationContextDto> GetOrRecoverContextAsync(Guid workspaceId, string phone, CancellationToken ct);
    Task SaveContextAsync(Guid workspaceId, string phone, ConversationContextDto context, CancellationToken ct);
    Task DeleteContextAsync(Guid workspaceId, string phone, CancellationToken ct);
}

public class ContextRecoveryService : IContextRecoveryService
{
    private readonly IConversationCache _cache;
    private readonly IConversationStateRepository _stateRepo;
    private readonly IClock? _clock;
    private readonly ILogger<ContextRecoveryService>? _logger;

    public ContextRecoveryService(IConversationCache cache, IConversationStateRepository stateRepo, IClock? clock = null,
        ILogger<ContextRecoveryService>? logger = null)
    {
        _cache = cache;
        _stateRepo = stateRepo;
        _clock = clock;
        _logger = logger;
    }

    public Task<ConversationContextDto> GetOrRecoverContextAsync(Guid workspaceId, string phone, CancellationToken ct)
        => ConversationDiagnostics.MeasureAsync(_logger, "ContextRecovery", () => RecoverCoreAsync(workspaceId, phone, ct));

    private async Task<ConversationContextDto> RecoverCoreAsync(Guid workspaceId, string phone, CancellationToken ct)
    {
        var cached = await _cache.GetContextAsync(workspaceId, phone, ct);
        // Validate even a cache hit: a failed cache write, concurrent refill or
        // restart must never make an older Redis value authoritative.
        var durable = await ConversationDiagnostics.MeasureAsync(_logger, "ContextStateRead", () => _stateRepo.GetStateAsync(workspaceId, phone, ct));
        if (durable == null)
        {
            if (cached != null) await _cache.DeleteContextAsync(workspaceId, phone, ct);
            return new ConversationContextDto();
        }
        if (cached?.StateVersion != null && cached.StateVersion == durable.StateVersion)
            return cached;

        await _cache.SetContextAsync(workspaceId, phone, durable, ct);
        return durable;
    }

    public async Task SaveContextAsync(Guid workspaceId, string phone, ConversationContextDto context, CancellationToken ct)
    {
        context.LastUpdated = _clock?.UtcNow ?? DateTime.UtcNow;
        await ConversationDiagnostics.MeasureAsync(_logger, "ContextStateWrite", async () => {
            await _stateRepo.UpsertStateAsync(workspaceId, phone, context, ct); return true;
        });
        await _cache.SetContextAsync(workspaceId, phone, context, ct);
    }

    public async Task DeleteContextAsync(Guid workspaceId, string phone, CancellationToken ct)
    {
        await _stateRepo.DeleteStateAsync(workspaceId, phone, ct);
        await _cache.DeleteContextAsync(workspaceId, phone, ct);
    }
}
