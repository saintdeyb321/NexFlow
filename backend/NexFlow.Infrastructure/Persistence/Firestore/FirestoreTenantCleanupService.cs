using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;

namespace NexFlow.Infrastructure.Persistence.Firestore;
public class FirestoreTenantCleanupService(FirestoreDb db, ILogger<FirestoreTenantCleanupService> logger) : ITenantCleanupService
{
    public async Task PurgeTenantDataAsync(Guid workspaceId, CancellationToken ct)
    {
        var root = db.Collection("workspaces").Document(workspaceId.ToString());
        await PurgeDocumentAsync(root, ct);
        logger.LogInformation("Firestore purged for workspace {WorkspaceId}", workspaceId);
    }

    private async Task PurgeDocumentAsync(DocumentReference document, CancellationToken ct)
    {
        await foreach (var collection in document.ListCollectionsAsync().WithCancellation(ct))
            // ListDocuments includes missing parent documents that still own subcollections.
            await foreach (var child in collection.ListDocumentsAsync().WithCancellation(ct))
                await PurgeDocumentAsync(child, ct);
        await document.DeleteAsync(cancellationToken: ct);
    }
}
