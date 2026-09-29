using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreCatalogRepository : ICatalogRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreCatalogRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    // =========================================================
    // CATEGORÍAS
    // =========================================================
    public async Task<IEnumerable<BusinessCategoryDto>> GetCategoriesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogCategories");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToCategoryDto).OrderBy(c => c.DisplayOrder);
    }

    public async Task<IEnumerable<BusinessCategoryDto>> GetActiveCategoriesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogCategories")
            .WhereEqualTo("IsActive", true);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToCategoryDto).OrderBy(c => c.DisplayOrder);
    }

    public async Task<BusinessCategoryDto?> GetCategoryByIdAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(categoryId)) return null;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogCategories").Document(categoryId);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? MapToCategoryDto(snapshot) : null;
    }

    public async Task SaveCategoryAsync(Guid workspaceId, BusinessCategoryDto category, CancellationToken cancellationToken)
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

        category.Name = category.Name?.Trim() ?? "";
        category.Scope = category.Scope?.Trim().ToUpperInvariant() ?? "";
        if (category.Name.Length == 0 || category.Scope is not ("PRODUCT" or "SERVICE" or "SHARED"))
            throw new DomainException("Nombre o scope de categoría inválido.");
        data.Name = category.Name;
        data.Scope = category.Scope;
        var workspace = docRef.Parent.Parent!;
        await _firestoreDb.RunTransactionAsync(async tx =>
        {
            await tx.GetSnapshotAsync(workspace, cancellationToken);
            var references = await tx.GetSnapshotAsync(workspace.Collection("catalogItems").WhereEqualTo("CategoryId", docId), cancellationToken);
            if (references.Documents.Select(MapToItemDto).Any(i =>
                (i.IsActive && !category.IsActive) || (category.Scope != "SHARED" && category.Scope != i.Type)))
                throw new DomainException("La categoría tiene offerings activos o de un tipo incompatible con el cambio.");
            tx.Set(docRef, data, SetOptions.MergeAll);
            tx.Set(workspace, new Dictionary<string, object> { ["businessRevision"] = Guid.NewGuid().ToString() }, SetOptions.MergeAll);
        }, cancellationToken: cancellationToken);
    }

    public async Task DeleteCategoryAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogCategories").Document(categoryId);
        var workspace = docRef.Parent.Parent!;
        await _firestoreDb.RunTransactionAsync(async tx =>
        {
            await tx.GetSnapshotAsync(workspace, cancellationToken);
            var references = await tx.GetSnapshotAsync(workspace.Collection("catalogItems").WhereEqualTo("CategoryId", categoryId).Limit(1), cancellationToken);
            if (references.Count > 0) throw new DomainException("La categoría sigue siendo utilizada por productos o servicios.");
            tx.Delete(docRef);
            tx.Set(workspace, new Dictionary<string, object> { ["businessRevision"] = Guid.NewGuid().ToString() }, SetOptions.MergeAll);
        }, cancellationToken: cancellationToken);
    }

    // =========================================================
    // ÍTEMS (PRODUCTOS Y SERVICIOS) - INFRAESTRUCTURA COMPARTIDA
    // =========================================================
    public async Task<IEnumerable<BusinessOfferingDto>> GetItemsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToItemDto);
    }

    public async Task<IEnumerable<BusinessOfferingDto>> GetActiveItemsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems")
            .WhereEqualTo("IsActive", true);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToItemDto);
    }

    public async Task<BusinessOfferingDto?> GetItemByIdAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(itemId)) return null;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems").Document(itemId);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);
        return snapshot.Exists ? MapToItemDto(snapshot) : null;
    }

    public async Task<IEnumerable<BusinessOfferingDto>> GetItemsByCategoryAsync(Guid workspaceId, string categoryId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems")
            .WhereEqualTo("CategoryId", categoryId);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToItemDto);
    }

    // 🔥 SPRINT 11 (Auditoría): Implementación de la búsqueda nativa por Tipo (PRODUCT o SERVICE) para ahorrar RAM
    public async Task<IEnumerable<BusinessOfferingDto>> GetItemsByTypeAsync(Guid workspaceId, string type, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems")
            .WhereEqualTo("Type", type.ToUpperInvariant());
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToItemDto);
    }

    public async Task SaveItemAsync(Guid workspaceId, BusinessOfferingDto item, CancellationToken cancellationToken)
    {
        item.Name = item.Name?.Trim() ?? "";
        item.Currency = item.Currency?.Trim().ToUpperInvariant() ?? "";
        item.LocationScope = item.LocationScope?.Trim().ToUpperInvariant() ?? "";
        item.CategoryId = item.CategoryId?.Trim() ?? "";
        if (item.CategoryId == Guid.Empty.ToString()) item.CategoryId = "";
        if (item.Name.Length == 0 || item.Currency.Length == 0 || item.PriceMinorUnits < 0)
            throw new DomainException("Nombre, precio o moneda inválidos.");
        if ((item is not ProductDto && item is not ServiceDto) || item.Type is not ("PRODUCT" or "SERVICE"))
            throw new DomainException("Tipo de offering inválido.");
        if (item.LocationScope is not ("ALL" or "SPECIFIC"))
            throw new DomainException("LocationScope debe ser ALL o SPECIFIC.");
        item.LocationIds = item.LocationScope == "ALL" ? new List<string>()
            : (item.LocationIds ?? new List<string>()).Select(id => id?.Trim() ?? "").Distinct(StringComparer.Ordinal).ToList();
        if (item.LocationScope == "SPECIFIC" && (item.LocationIds.Count == 0 || item.LocationIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Contains('/'))))
            throw new DomainException("SPECIFIC requiere al menos una sede válida.");
        if (item.CategoryId.Contains('/') || item.Id?.Contains('/') == true)
            throw new DomainException("ID de offering o categoría inválido.");
        if (item is ServiceDto service)
        {
            if (service.RequiresReservation && (!service.DurationInMinutes.HasValue || service.DurationInMinutes < 5))
                throw new DomainException("Un servicio reservable requiere una duración de al menos 5 minutos.");
            if (!service.RequiresReservation) service.DurationInMinutes = null;
        }
        var docId = string.IsNullOrEmpty(item.Id) ? Guid.NewGuid().ToString() : item.Id;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems").Document(docId);

        var serviceData = item as ServiceDto;

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

            DurationInMinutes = serviceData?.DurationInMinutes,
            RequiresReservation = serviceData?.RequiresReservation ?? false,
            ImageUrl = item.ImageUrl,
            Metadata = item.Metadata ?? new Dictionary<string, object>()
        };

        var workspace = docRef.Parent.Parent!;
        await _firestoreDb.RunTransactionAsync(async tx =>
        {
            await tx.GetSnapshotAsync(workspace, cancellationToken);
            var existing = await tx.GetSnapshotAsync(docRef, cancellationToken);
            if (existing.Exists && MapToItemDto(existing).Type != item.Type)
                throw new DomainException("No se puede cambiar el tipo del offering.");
            foreach (var locationId in item.LocationIds)
            {
                var location = await tx.GetSnapshotAsync(workspace.Collection("locations").Document(locationId), cancellationToken);
                if (!location.Exists) throw new DomainException("Una de las sedes no existe en este workspace.");
            }
            if (item.CategoryId.Length > 0)
            {
                var categoryDoc = await tx.GetSnapshotAsync(workspace.Collection("catalogCategories").Document(item.CategoryId), cancellationToken);
                if (!categoryDoc.Exists) throw new DomainException("La categoría no existe en este workspace.");
                var category = MapToCategoryDto(categoryDoc);
                if ((item.IsActive && !category.IsActive) || (category.Scope != "SHARED" && category.Scope != item.Type))
                    throw new DomainException("La categoría está inactiva o tiene un scope incompatible.");
            }
            // Full replacement also removes legacy location references.
            tx.Set(docRef, data);
            tx.Set(workspace, new Dictionary<string, object> { ["businessRevision"] = Guid.NewGuid().ToString() }, SetOptions.MergeAll);
        }, cancellationToken: cancellationToken);
    }

    public async Task DeleteItemAsync(Guid workspaceId, string itemId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalogItems").Document(itemId);
        await docRef.UpdateAsync(new Dictionary<string, object> { ["IsActive"] = false }, cancellationToken: cancellationToken);
    }

    // =========================================================
    // MAPPERS Y CLASES INTERNAS FIRESTORE
    // =========================================================
    private static BusinessCategoryDto MapToCategoryDto(DocumentSnapshot doc)
    {
        var data = doc.ConvertTo<FirestoreCatalogCategory>();
        return new BusinessCategoryDto
        {
            Id = doc.Id,
            Name = data.Name,
            Description = data.Description,
            Scope = data.Scope ?? "SHARED",
            IsActive = data.IsActive,
            DisplayOrder = data.DisplayOrder
        };
    }

    private static BusinessOfferingDto MapToItemDto(DocumentSnapshot doc)
    {
        var data = doc.ConvertTo<FirestoreCatalogItem>();

        string finalScope = data.LocationScope ?? "ALL";
        List<string> finalIds = data.LocationIds ?? new List<string>();

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

        string type = (data.Type ?? "PRODUCT").ToUpperInvariant();

        if (type == "SERVICE")
        {
            return new ServiceDto
            {
                Id = doc.Id,
                CategoryId = data.CategoryId,
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

        return new ProductDto
        {
            Id = doc.Id,
            CategoryId = data.CategoryId,
            Name = data.Name,
            Description = data.Description,
            PriceMinorUnits = data.PriceMinorUnits,
            Currency = data.Currency,
            IsActive = data.IsActive,
            LocationScope = finalScope,
            LocationIds = finalIds,
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

        [FirestoreProperty("AvailableAtLocations")] public List<string>? LegacyAvailableAtLocations { get; set; }

        [FirestoreProperty] public int? DurationInMinutes { get; set; }
        [FirestoreProperty] public bool RequiresReservation { get; set; }
        [FirestoreProperty] public string? ImageUrl { get; set; }
        [FirestoreProperty] public Dictionary<string, object> Metadata { get; set; } = new();
    }
}

