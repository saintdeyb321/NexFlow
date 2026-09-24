    using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Abstractions;

public interface ICatalogHashService
{
    string ComputeHash(IEnumerable<CatalogCategoryDto> categories, IEnumerable<BusinessOfferingDto> items);
}