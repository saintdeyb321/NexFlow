using System;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreTenantCleanupService : ITenantCleanupService
{
    private readonly FirestoreDb _db;

    public FirestoreTenantCleanupService(FirestoreDb db)
    {
        _db = db;
    }

    public async Task PurgeTenantDataAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var workspaceRef = _db.Collection("workspaces").Document(workspaceId.ToString());

        // 1. ELIMINACIÓN DINÁMICA: Listar todas las subcolecciones existentes de forma automática
        var collections = await workspaceRef.ListCollectionsAsync().ToListAsync();

        foreach (var collectionRef in collections)
        {
            // Para cada subcolección (sea faqs, services, reservations, conversations, etc.)
            var snapshot = await collectionRef.GetSnapshotAsync(cancellationToken);

            foreach (var doc in snapshot.Documents)
            {
                // Si la colección tiene documentos anidados (ej. conversaciones que contienen mensajes)
                var nestedCollections = await doc.Reference.ListCollectionsAsync().ToListAsync();
                foreach (var nestedCol in nestedCollections)
                {
                    await DeleteEntireCollectionAsync(nestedCol, cancellationToken);
                }

                await doc.Reference.DeleteAsync(Precondition.None, cancellationToken);
            }
        }

        // 2. Finalmente borrar el documento maestro del Tenant
        await workspaceRef.DeleteAsync(Precondition.None, cancellationToken);
    }

    // Método auxiliar recursivo para colecciones anidadas profundamente
    private async Task DeleteEntireCollectionAsync(CollectionReference collectionRef, CancellationToken cancellationToken)
    {
        var snapshot = await collectionRef.GetSnapshotAsync(cancellationToken);
        foreach (var doc in snapshot.Documents)
        {
            var deeperCollections = await doc.Reference.ListCollectionsAsync().ToListAsync();
            foreach (var deepCol in deeperCollections)
            {
                await DeleteEntireCollectionAsync(deepCol, cancellationToken);
            }
            await doc.Reference.DeleteAsync(Precondition.None, cancellationToken);
        }
    }
}