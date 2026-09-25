using NexFlow.Application.Features.Shared.DTOs;

namespace NexFlow.Application.Features.Catalog.DTOs;

public class ProductDto : BusinessOfferingDto
{
    public ProductDto()
    {
        Type = "PRODUCT";
    }
}