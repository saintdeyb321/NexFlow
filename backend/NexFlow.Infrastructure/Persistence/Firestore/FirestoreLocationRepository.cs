using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;

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
            return new LocationDto(doc.Id, data.Name, data.Address, data.Reference, data.MapUrl, data.IsMain);
        });
    }

    public async Task SaveLocationAsync(Guid workspaceId, LocationDto location, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(location.Id) ? Guid.NewGuid().ToString() : location.Id;

        // 🔥 SPRINT 16 (P0): Validación estricta anti-duplicación de URLs de Maps
        if (!string.IsNullOrWhiteSpace(location.MapUrl))
        {
            var allLocations = await GetLocationsAsync(workspaceId, cancellationToken);
            var duplicateMap = allLocations.FirstOrDefault(l => l.Id != docId && l.MapUrl == location.MapUrl);

            if (duplicateMap != null)
            {
                throw new InvalidOperationException($"La URL del mapa pertenece actualmente a la sede '{duplicateMap.Name}'. No se permiten enlaces de mapa duplicados en el mismo negocio.");
            }
        }

        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("locations").Document(docId);

        var data = new FirestoreLocation
        {
            Name = location.Name,
            Address = location.Address,
            Reference = location.Reference ?? string.Empty,
            MapUrl = location.MapUrl,
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
        [FirestoreProperty] public string? MapUrl { get; set; }
        [FirestoreProperty] public bool IsMain { get; set; }
    }
}