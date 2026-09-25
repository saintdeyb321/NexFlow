// 🔥 NUEVO NAMESPACE
using NexFlow.Application.Features.Shared.DTOs;

namespace NexFlow.Application.Abstractions;

public interface ICatalogHashService
{
    string ComputeHash(IEnumerable<BusinessCategoryDto> categories, IEnumerable<BusinessOfferingDto> items);
}