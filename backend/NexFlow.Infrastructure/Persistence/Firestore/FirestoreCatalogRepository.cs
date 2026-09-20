using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreCatalogRepository : ICatalogRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreCatalogRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    // =========================================================
    // CATEGORÍAS
    // =========================================================
    public async Task<IEnumerable<CatalogCategoryDto>> GetCategoriesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogCategories");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToCategoryDto).OrderBy(c => c.DisplayOrder);
    }

    public async Task<IEnumerable<CatalogCategoryDto>> GetActiveCategoriesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogCategories")
            .WhereEqualTo("IsActive", true);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToCategoryDto).OrderBy(c => c.DisplayOrder);
    }

    public async Task<CatalogCategoryDto?> GetCategoryByIdAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(categoryId)) return null;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogCategories").Document(categoryId);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? MapToCategoryDto(snapshot) : null;
    }

    public async Task SaveCategoryAsync(Guid workspaceId, CatalogCategoryDto category, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(category.Id) ? Guid.NewGuid().ToString() : category.Id;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogCategories").Document(docId);

        var data = new FirestoreCatalogCategory
        {
            Name = category.Name,
            Description = category.Description,
            Scope = category.Scope,
            IsActive = category.IsActive,
            DisplayOrder = category.DisplayOrder
        };

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

    public async Task DeleteCategoryAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogCategories").Document(categoryId);
        await docRef.DeleteAsync(Precondition.None, cancellationToken);
    }

    // =========================================================
    // ÍTEMS (PRODUCTOS Y SERVICIOS)
    // =========================================================
    public async Task<IEnumerable<CatalogItemDto>> GetItemsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToItemDto);
    }

    public async Task<IEnumerable<CatalogItemDto>> GetActiveItemsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems")
            .WhereEqualTo("IsActive", true);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToItemDto);
    }

    public async Task<CatalogItemDto?> GetItemByIdAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems").Document(itemId);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? MapToItemDto(snapshot) : null;
    }

    public async Task<IEnumerable<CatalogItemDto>> GetItemsByCategoryAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems")
            .WhereEqualTo("CategoryId", categoryId)
            .WhereEqualTo("IsActive", true);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToItemDto);
    }

    public async Task SaveItemAsync(Guid workspaceId, CatalogItemDto item, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(item.Id) ? Guid.NewGuid().ToString() : item.Id;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems").Document(docId);

        var data = new FirestoreCatalogItem
        {
            CategoryId = item.CategoryId,
            Type = item.Type.ToUpperInvariant(),
            Name = item.Name,
            Description = item.Description,
            PriceMinorUnits = item.PriceMinorUnits,
            Currency = item.Currency,
            IsActive = item.IsActive,

            LocationScope = item.LocationScope ?? "ALL",
            LocationIds = item.LocationIds ?? new List<string>(),

            DurationInMinutes = item.Type == "SERVICE" ? item.DurationInMinutes : null,
            RequiresReservation = item.Type == "SERVICE" && item.RequiresReservation,
            ImageUrl = item.ImageUrl,
            Metadata = item.Metadata ?? new Dictionary<string, object>()
        };

        // Al usar SetOptions.MergeAll, eventualmente se podría usar Update para borrar AvailableAtLocations,
        // pero escribir la nueva estructura es suficiente por ahora.
        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

    public async Task DeleteItemAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems").Document(itemId);
        await docRef.DeleteAsync(Precondition.None, cancellationToken);
    }

    // =========================================================
    // MAPPERS Y CLASES INTERNAS FIRESTORE
    // =========================================================
    private static CatalogCategoryDto MapToCategoryDto(DocumentSnapshot doc)
    {
        var data = doc.ConvertTo<FirestoreCatalogCategory>();
        return new CatalogCategoryDto
        {
            Id = doc.Id,
            Name = data.Name,
            Description = data.Description,
            Scope = data.Scope ?? "SHARED",
            IsActive = data.IsActive,
            DisplayOrder = data.DisplayOrder
        };
    }

    private static CatalogItemDto MapToItemDto(DocumentSnapshot doc)
    {
        var data = doc.ConvertTo<FirestoreCatalogItem>();

        // 🔥 SPRINT A1: Migración al vuelo (On-The-Fly Migration)
        string finalScope = data.LocationScope ?? "ALL";
        List<string> finalIds = data.LocationIds ?? new List<string>();

        // Si el documento es legacy (no tiene LocationScope pero sí tiene AvailableAtLocations)
        if (!doc.ContainsField("LocationScope") && doc.ContainsField("AvailableAtLocations"))
        {
            if (data.LegacyAvailableAtLocations != null && data.LegacyAvailableAtLocations.Any())
            {
                finalScope = "SPECIFIC";
                finalIds = data.LegacyAvailableAtLocations;
            }
            else
            {
                finalScope = "ALL";
                finalIds = new List<string>();
            }
        }

        return new CatalogItemDto
        {
            Id = doc.Id,
            CategoryId = data.CategoryId,
            Type = data.Type ?? "PRODUCT",
            Name = data.Name,
            Description = data.Description,
            PriceMinorUnits = data.PriceMinorUnits,
            Currency = data.Currency,
            IsActive = data.IsActive,
            LocationScope = finalScope,
            LocationIds = finalIds,
            DurationInMinutes = data.DurationInMinutes,
            RequiresReservation = data.RequiresReservation,
            ImageUrl = data.ImageUrl,
            Metadata = data.Metadata ?? new Dictionary<string, object>()
        };
    }

    [FirestoreData]
    private class FirestoreCatalogCategory
    {
        [FirestoreProperty] public string Name { get; set; } = string.Empty;
        [FirestoreProperty] public string? Description { get; set; }
        [FirestoreProperty] public string Scope { get; set; } = "SHARED";
        [FirestoreProperty] public bool IsActive { get; set; } = true;
        [FirestoreProperty] public int DisplayOrder { get; set; } = 0;
    }

    [FirestoreData]
    private class FirestoreCatalogItem
    {
        [FirestoreProperty] public string CategoryId { get; set; } = string.Empty;
        [FirestoreProperty] public string Type { get; set; } = "PRODUCT";
        [FirestoreProperty] public string Name { get; set; } = string.Empty;
        [FirestoreProperty] public string? Description { get; set; }
        [FirestoreProperty] public long PriceMinorUnits { get; set; }
        [FirestoreProperty] public string Currency { get; set; } = "PEN";
        [FirestoreProperty] public bool IsActive { get; set; } = true;

        [FirestoreProperty] public string LocationScope { get; set; } = "ALL";
        [FirestoreProperty] public List<string> LocationIds { get; set; } = new();

        // 🔥 Retenemos el campo legacy solo para lectura durante la migración
        [FirestoreProperty("AvailableAtLocations")] public List<string>? LegacyAvailableAtLocations { get; set; }

        [FirestoreProperty] public int? DurationInMinutes { get; set; }
        [FirestoreProperty] public bool RequiresReservation { get; set; }
        [FirestoreProperty] public string? ImageUrl { get; set; }
        [FirestoreProperty] public Dictionary<string, object> Metadata { get; set; } = new();
    }
}