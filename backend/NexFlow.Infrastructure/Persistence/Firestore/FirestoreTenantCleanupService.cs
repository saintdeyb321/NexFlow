using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreTenantCleanupService : ITenantCleanupService
{
    private readonly FirestoreDb _db;
    private readonly ILogger<FirestoreTenantCleanupService> _logger;

    public FirestoreTenantCleanupService(FirestoreDb db, ILogger<FirestoreTenantCleanupService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task PurgeTenantDataAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Iniciando Job asíncrono de purga masiva de datos en Firestore para Workspace: {WorkspaceId}", workspaceId);

        var workspaceRef = _db.Collection("workspaces").Document(workspaceId.ToString());

        try
        {
            var collections = await workspaceRef.ListCollectionsAsync().ToListAsync();

            foreach (var collectionRef in collections)
            {
                var snapshot = await collectionRef.GetSnapshotAsync(cancellationToken);
                foreach (var doc in snapshot.Documents)
                {
                    var nestedCollections = await doc.Reference.ListCollectionsAsync().ToListAsync();
                    foreach (var nestedCol in nestedCollections)
                    {
                        await DeleteEntireCollectionAsync(nestedCol, cancellationToken);
                    }
                    await doc.Reference.DeleteAsync(Precondition.None, cancellationToken);
                }
            }

            await workspaceRef.DeleteAsync(Precondition.None, cancellationToken);
            _logger.LogInformation("Purga masiva de Firestore completada con éxito para Workspace: {WorkspaceId}", workspaceId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo crítico durante la purga en segundo plano del Workspace: {WorkspaceId}", workspaceId);
            throw; 
        }
    }

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