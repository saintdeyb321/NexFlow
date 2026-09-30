using NexFlow.Application.Features.Shared.DTOs;

namespace NexFlow.Application.Abstractions;

public interface ICatalogRepository
{
    Task<int> CountActiveItemsByTypeAsync(Guid workspaceId, string type, CancellationToken ct);
    // ==========================================
    // GESTIÓN DE CATEGORÍAS (Infraestructura Compartida)
    // ==========================================
    Task<IEnumerable<BusinessCategoryDto>> GetCategoriesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<IEnumerable<BusinessCategoryDto>> GetActiveCategoriesAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<BusinessCategoryDto?> GetCategoryByIdAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken);
    Task SaveCategoryAsync(Guid workspaceId, BusinessCategoryDto category, bool isCreate, CancellationToken cancellationToken);
    Task DeleteCategoryAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken);

    // ==========================================
    // GESTIÓN DE ÍTEMS (Infraestructura Compartida)
    // ==========================================
    Task<IEnumerable<BusinessOfferingDto>> GetItemsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<IEnumerable<BusinessOfferingDto>> GetActiveItemsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<BusinessOfferingDto?> GetItemByIdAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken);
    Task<IEnumerable<BusinessOfferingDto>> GetItemsByCategoryAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken);

    Task<IEnumerable<BusinessOfferingDto>> GetItemsByTypeAsync(Guid workspaceId, string type, CancellationToken cancellationToken);

    Task SaveItemAsync(Guid workspaceId, BusinessOfferingDto item, bool isCreate, CancellationToken cancellationToken);
    Task DeleteItemAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken);
}
