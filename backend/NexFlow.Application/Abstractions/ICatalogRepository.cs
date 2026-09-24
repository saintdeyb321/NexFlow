using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Abstractions;

public interface ICatalogRepository
{
    // ==========================================
    // GESTIÓN DE CATEGORÍAS
    // ==========================================
    Task<IEnumerable<CatalogCategoryDto>> GetCategoriesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<IEnumerable<CatalogCategoryDto>> GetActiveCategoriesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<CatalogCategoryDto?> GetCategoryByIdAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken);
    Task SaveCategoryAsync(Guid workspaceId, CatalogCategoryDto category, CancellationToken cancellationToken);
    Task DeleteCategoryAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken);

    // ==========================================
    // GESTIÓN DE ÍTEMS (Infraestructura Compartida)
    // ==========================================
    Task<IEnumerable<BusinessOfferingDto>> GetItemsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<IEnumerable<BusinessOfferingDto>> GetActiveItemsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<BusinessOfferingDto?> GetItemByIdAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken);
    Task<IEnumerable<BusinessOfferingDto>> GetItemsByCategoryAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken);
    Task SaveItemAsync(Guid workspaceId, BusinessOfferingDto item, CancellationToken cancellationToken);
    Task DeleteItemAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken);
}