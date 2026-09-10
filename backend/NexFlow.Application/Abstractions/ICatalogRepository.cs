using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
    // GESTIÓN DE ÍTEMS (Productos y Servicios)
    // ==========================================
    Task<IEnumerable<CatalogItemDto>> GetItemsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<IEnumerable<CatalogItemDto>> GetActiveItemsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<CatalogItemDto?> GetItemByIdAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken);
    Task<IEnumerable<CatalogItemDto>> GetItemsByCategoryAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken);
    Task SaveItemAsync(Guid workspaceId, CatalogItemDto item, CancellationToken cancellationToken);
    Task DeleteItemAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken);
}