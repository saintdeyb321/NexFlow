using NexFlow.Application.Features.Knowledge;

namespace NexFlow.Application.Features.Business.Locations;

public interface ILocationResolverService
{
    Task<string?> ResolveLocationIdAsync(Guid workspaceId, string spokenLocation, CancellationToken ct);
}

public class LocationResolverService : ILocationResolverService
{
    private readonly IKnowledgeService _knowledgeService;

    public LocationResolverService(IKnowledgeService knowledgeService)
    {
        _knowledgeService = knowledgeService;
    }

    public async Task<string?> ResolveLocationIdAsync(Guid workspaceId, string spokenLocation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(spokenLocation)) return null;

        var snapshot = await _knowledgeService.GetSnapshotAsync(workspaceId, ct);
        var term = spokenLocation.ToLowerInvariant();

        // 1. Busca coincidencia exacta
        var exactMatch = snapshot.Locations.FirstOrDefault(l => l.Name.ToLowerInvariant() == term);
        if (exactMatch != null) return exactMatch.Id;

        // 2. Busca coincidencia parcial (ej: "principal" hace match con "Sede Principal")
        var partialMatch = snapshot.Locations.FirstOrDefault(l => l.Name.ToLowerInvariant().Contains(term) || (l.IsMain && term.Contains("principal")));

        return partialMatch?.Id;
    }
}