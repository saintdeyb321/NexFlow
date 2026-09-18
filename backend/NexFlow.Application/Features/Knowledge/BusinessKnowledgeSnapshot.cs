using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Features.Knowledge;

/// <summary>
/// Representa una foto instantánea de toda la información del negocio.
/// Esto evita consultar Firebase múltiples veces durante una misma conversación.
/// </summary>
public class BusinessKnowledgeSnapshot
{
    public Guid WorkspaceId { get; init; }
    public BusinessProfileDto? Profile { get; init; }
    public IReadOnlyList<LocationDto> Locations { get; init; } = Array.Empty<LocationDto>();
    public IReadOnlyList<BusinessHoursDto> Hours { get; init; } = Array.Empty<BusinessHoursDto>();
    public IReadOnlyList<FaqDto> Faqs { get; init; } = Array.Empty<FaqDto>();
    public IReadOnlyList<CatalogItemDto> CatalogItems { get; init; } = Array.Empty<CatalogItemDto>();

    public DateTime RetrievedAt { get; init; } = DateTime.UtcNow;

    // Ayudante rápido para verificar si hay datos
    public bool HasLocations => Locations.Any();
    public bool HasCatalog => CatalogItems.Any();
}