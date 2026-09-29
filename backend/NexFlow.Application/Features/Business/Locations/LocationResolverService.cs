using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Knowledge;

namespace NexFlow.Application.Features.Business.Locations;

public interface ILocationResolverService
{
    Task<string?> ResolveLocationIdAsync(Guid workspaceId, string spokenLocation, CancellationToken ct);
}

public class LocationResolverService : ILocationResolverService
{
    private readonly ILocationRepository _locationRepository;

    public LocationResolverService(ILocationRepository locationRepository)
    {
        _locationRepository = locationRepository;
    }

    public async Task<string?> ResolveLocationIdAsync(Guid workspaceId, string spokenLocation, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(spokenLocation)) return null;

        var locations = await _locationRepository.GetLocationsAsync(workspaceId, ct);
        var term = spokenLocation.ToLowerInvariant();

        // 1. Busca coincidencia exacta
        var exactMatch = locations.FirstOrDefault(l => l.Name.ToLowerInvariant() == term);
        if (exactMatch != null) return exactMatch.Id;

        // 2. Busca coincidencia parcial (ej: "principal" hace match con "Sede Principal")
        var partialMatch = locations.FirstOrDefault(l => l.Name.ToLowerInvariant().Contains(term) || (l.IsMain && term.Contains("principal")));

        return partialMatch?.Id;
    }
}
