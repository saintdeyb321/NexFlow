using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Services.DTOs;

namespace NexFlow.Application.Features.Knowledge;

public class BusinessKnowledgeSnapshot
{
    public Guid WorkspaceId { get; init; }
    public BusinessProfileDto? Profile { get; init; }
    public IReadOnlyList<LocationDto> Locations { get; init; } = Array.Empty<LocationDto>();
    public IReadOnlyList<BusinessHoursDto> Hours { get; init; } = Array.Empty<BusinessHoursDto>();
    public IReadOnlyList<FaqDto> Faqs { get; init; } = Array.Empty<FaqDto>();

    // 🔥 SPRINT 04: Separación estricta de dominios en la memoria
    public IReadOnlyList<ProductDto> Products { get; init; } = Array.Empty<ProductDto>();
    public IReadOnlyList<ServiceDto> Services { get; init; } = Array.Empty<ServiceDto>();

    public DateTime RetrievedAt { get; init; } = DateTime.UtcNow;

    public bool HasLocations => Locations.Any();
    public bool HasProducts => Products.Any();
    public bool HasServices => Services.Any();
}