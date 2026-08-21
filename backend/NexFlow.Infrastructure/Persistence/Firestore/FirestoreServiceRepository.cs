using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business; // Importante: ServiceDto ahora vive aquí
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreServiceRepository : IServiceRepository
{
    private readonly FirestoreDb _firestoreDb;
    public FirestoreServiceRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    public async Task<IEnumerable<ServiceDto>> GetServicesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("services");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Select(doc =>
        {
            var data = doc.ConvertTo<FirestoreService>();

            // Usamos inicialización de propiedades, el Id ya es un string nativo
            return new ServiceDto
            {
                Id = doc.Id,
                Name = data.Name,
                DurationInMinutes = data.DurationInMinutes
            };
        });
    }

    public async Task SaveServiceAsync(Guid workspaceId, ServiceDto service, CancellationToken cancellationToken)
    {
        // Evaluamos el string (null o vacío) en lugar de Guid.Empty
        var docId = string.IsNullOrEmpty(service.Id) ? Guid.NewGuid().ToString() : service.Id;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("services").Document(docId);

        var data = new FirestoreService { Name = service.Name, DurationInMinutes = service.DurationInMinutes };
        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

    public async Task DeleteServiceAsync(Guid workspaceId, string serviceId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("services").Document(serviceId);
        await docRef.DeleteAsync(Precondition.None, cancellationToken);
    }

    [FirestoreData]
    private class FirestoreService
    {
        [FirestoreProperty] public string Name { get; set; } = string.Empty;
        [FirestoreProperty] public int DurationInMinutes { get; set; }
    }
}