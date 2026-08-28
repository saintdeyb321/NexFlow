using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreCatalogRepository : ICatalogRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreCatalogRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    public async Task<IEnumerable<ProductDto>> GetProductsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalog");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Select(doc =>
        {
            var data = doc.ConvertTo<FirestoreProduct>();

            return new ProductDto
            {
                Id = doc.Id,
                Name = data.Name,
                Description = data.Description,
                Category = data.Category,
                PriceMinorUnits = data.PriceMinorUnits, // 🔥 Parseo limpio sin comas
                Currency = data.Currency,
                IsActive = data.IsActive,
                Metadata = data.Metadata ?? new Dictionary<string, object>()
            };
        });
    }

    public async Task SaveProductAsync(Guid workspaceId, ProductDto product, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(product.Id) ? Guid.NewGuid().ToString() : product.Id;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalog").Document(docId);

        var data = new FirestoreProduct
        {
            Name = product.Name,
            Description = product.Description,
            Category = product.Category,
            PriceMinorUnits = product.PriceMinorUnits, // 🔥 Persistencia de entero
            Currency = product.Currency,
            IsActive = product.IsActive,
            Metadata = product.Metadata
        };

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

    public async Task DeleteProductAsync(Guid workspaceId, string productId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("catalog").Document(productId);
        await docRef.DeleteAsync(Precondition.None, cancellationToken);
    }

    [FirestoreData]
    private class FirestoreProduct
    {
        [FirestoreProperty] public string Name { get; set; } = string.Empty;
        [FirestoreProperty] public string? Description { get; set; }
        [FirestoreProperty] public string? Category { get; set; }
        [FirestoreProperty] public long PriceMinorUnits { get; set; }
        [FirestoreProperty] public string Currency { get; set; } = "PEN";
        [FirestoreProperty] public bool IsActive { get; set; } = true;
        [FirestoreProperty] public Dictionary<string, object> Metadata { get; set; } = new();
    }
}