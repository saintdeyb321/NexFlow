using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreLocationRepository : ILocationRepository
{
    private readonly FirestoreDb _firestoreDb;
    public FirestoreLocationRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    public async Task<IEnumerable<LocationDto>> GetLocationsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("locations");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Select(doc =>
        {
            var data = doc.ConvertTo<FirestoreLocation>();
            return new LocationDto(doc.Id, data.Name, data.Address, data.Reference, data.IsMain);
        });
    }

    public async Task SaveLocationAsync(Guid workspaceId, LocationDto location, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(location.Id) ? Guid.NewGuid().ToString() : location.Id;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("locations").Document(docId);

        var data = new FirestoreLocation
        {
            Name = location.Name,
            Address = location.Address,
            Reference = location.Reference,
            IsMain = location.IsMain
        };

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

    public async Task DeleteLocationAsync(Guid workspaceId, string locationId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("locations").Document(locationId);
        await docRef.DeleteAsync(Precondition.None, cancellationToken);
    }

    [FirestoreData]
    private class FirestoreLocation
    {
        [FirestoreProperty] public string Name { get; set; } = string.Empty;
        [FirestoreProperty] public string Address { get; set; } = string.Empty;
        [FirestoreProperty] public string Reference { get; set; } = string.Empty;
        [FirestoreProperty] public bool IsMain { get; set; }
    }
}