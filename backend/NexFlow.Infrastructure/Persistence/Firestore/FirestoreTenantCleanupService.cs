using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using System.Collections.Generic;
using System.Linq;

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
        _logger.LogInformation("Iniciando estado de purga (InProgress) en Firestore para Workspace: {WorkspaceId}", workspaceId);

        var workspaceRef = _db.Collection("workspaces").Document(workspaceId.ToString());
        var docsToDelete = new List<DocumentReference>();

        try
        {
            // 1. Recolectar todas las referencias de forma recursiva
            await CollectDocumentsToDeleteAsync(workspaceRef, docsToDelete, cancellationToken);

            // 🔥 SPRINT 20: EXTERMINIO EXPLÍCITO DE DOCUMENTOS FANTASMA (Implicit Documents)
            // Agregamos manualmente las rutas críticas que ListCollectionsAsync podría omitir si no tienen campos raíz
            string[] explicitCollections = { "items", "categories", "orders", "reservations", "requests", "conversations", "artifacts", "hours" };

            foreach (var col in explicitCollections)
            {
                var snapshot = await workspaceRef.Collection(col).GetSnapshotAsync(cancellationToken);
                foreach (var doc in snapshot.Documents)
                {
                    if (!docsToDelete.Contains(doc.Reference))
                    {
                        docsToDelete.Add(doc.Reference);
                    }
                }
            }

            // Exterminio directo del documento schedule fantasma
            var scheduleRef = workspaceRef.Collection("hours").Document("schedule");
            if (!docsToDelete.Contains(scheduleRef)) docsToDelete.Add(scheduleRef);

            // 2. Agregar el documento raíz del tenant al final
            if (!docsToDelete.Contains(workspaceRef)) docsToDelete.Add(workspaceRef);

            // 3. Procesar en lotes de 500 (Límite estricto de Firestore)
            const int batchSize = 500;
            for (int i = 0; i < docsToDelete.Count; i += batchSize)
            {
                var batch = _db.StartBatch();
                var currentBatchDocs = docsToDelete.Skip(i).Take(batchSize);

                foreach (var doc in currentBatchDocs)
                {
                    batch.Delete(doc, Precondition.None);
                }

                await batch.CommitAsync(cancellationToken);
            }

            _logger.LogInformation("Purga masiva completada con éxito. Se eliminaron {Count} documentos.", docsToDelete.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fallo crítico durante la purga de Firestore del Workspace: {WorkspaceId}", workspaceId);
            throw;
        }
    }

    private async Task CollectDocumentsToDeleteAsync(DocumentReference parentRef, List<DocumentReference> docsToDelete, CancellationToken cancellationToken)
    {
        var collections = await parentRef.ListCollectionsAsync().ToListAsync();

        foreach (var collection in collections)
        {
            var snapshot = await collection.GetSnapshotAsync(cancellationToken);
            foreach (var doc in snapshot.Documents)
            {
                // Entrar a subcolecciones antes de marcar el documento actual
                await CollectDocumentsToDeleteAsync(doc.Reference, docsToDelete, cancellationToken);

                docsToDelete.Add(doc.Reference);
            }
        }
    }
}