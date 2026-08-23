using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Abstractions;

public interface ICatalogRepository
{
    Task<IEnumerable<ProductDto>> GetProductsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task SaveProductAsync(Guid workspaceId, ProductDto product, CancellationToken cancellationToken);
    Task DeleteProductAsync(Guid workspaceId, string productId, CancellationToken cancellationToken);
}