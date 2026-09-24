using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Features.Knowledge;

public class BusinessKnowledgeSnapshot
{
    public Guid WorkspaceId { get; init; }
    public BusinessProfileDto? Profile { get; init; }
    public IReadOnlyList<LocationDto> Locations { get; init; } = Array.Empty<LocationDto>();
    public IReadOnlyList<BusinessHoursDto> Hours { get; init; } = Array.Empty<BusinessHoursDto>();
    public IReadOnlyList<FaqDto> Faqs { get; init; } = Array.Empty<FaqDto>();

    // 🔥 SPRINT 1: Se usa BusinessOfferingDto para contener tanto productos como servicios en la memoria de la IA
    public IReadOnlyList<BusinessOfferingDto> Offerings { get; init; } = Array.Empty<BusinessOfferingDto>();

    public DateTime RetrievedAt { get; init; } = DateTime.UtcNow;

    public bool HasLocations => Locations.Any();
    public bool HasOfferings => Offerings.Any();
}