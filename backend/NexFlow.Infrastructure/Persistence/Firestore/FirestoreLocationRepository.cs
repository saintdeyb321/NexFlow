using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
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
        return snapshot.Documents.Select(Map);
    }

    public async Task<Result> SaveLocationAsync(Guid workspaceId, LocationDto location, bool create, int maxLocations, CancellationToken cancellationToken)
    {
        var id = string.IsNullOrWhiteSpace(location.Id) && create ? Guid.NewGuid().ToString() : location.Id;
        if (string.IsNullOrWhiteSpace(id) || id.Contains('/'))
            return Result.Failure(new Error("Location.Invalid", "ID de sede inválido."));
        var workspace = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString());
        return await _firestoreDb.RunTransactionAsync(async tx =>
        {
            // All business-integrity writers touch this document, including on an empty collection.
            await tx.GetSnapshotAsync(workspace, cancellationToken);
            var snapshot = await tx.GetSnapshotAsync(workspace.Collection("locations"), cancellationToken);
            var locations = snapshot.Documents.Select(Map).ToList();
            var exists = locations.Any(l => l.Id == id);
            if (create && exists) return Result.Failure(new Error("Location.Conflict", "La sede ya existe."));
            if (!create && !exists) return Result.Failure(new Error("Location.NotFound", "La sede no existe."));
            if (create && locations.Count >= maxLocations)
                return Result.Failure(new Error("Location.LimitReached", "Límite de sedes alcanzado."));
            if (!string.IsNullOrWhiteSpace(location.MapUrl) && locations.Any(l => l.Id != id && l.MapUrl == location.MapUrl))
                return Result.Failure(new Error("Location.Conflict", "El enlace de mapa ya pertenece a otra sede."));

            var mainId = location.IsMain ? id : locations.Where(l => l.IsMain).OrderBy(l => l.Id, StringComparer.Ordinal).Select(l => l.Id).FirstOrDefault() ?? id;
            foreach (var doc in snapshot.Documents.Where(d => d.Id != id))
                tx.Update(doc.Reference, new Dictionary<string, object> { ["IsMain"] = doc.Id == mainId });
            tx.Set(workspace.Collection("locations").Document(id), new FirestoreLocation
            {
                Name = location.Name, Address = location.Address, Reference = location.Reference ?? "",
                MapUrl = location.MapUrl, IsMain = id == mainId
            });
            tx.Set(workspace, new Dictionary<string, object> { ["businessRevision"] = Guid.NewGuid().ToString() }, SetOptions.MergeAll);
            return Result.Success();
        }, cancellationToken: cancellationToken);
    }

    public async Task<Result> DeleteLocationAsync(Guid workspaceId, string locationId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(locationId) || locationId.Contains('/'))
            return Result.Failure(new Error("Location.Invalid", "ID de sede inválido."));
        var workspace = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString());
        return await _firestoreDb.RunTransactionAsync(async tx =>
        {
            await tx.GetSnapshotAsync(workspace, cancellationToken);
            var locations = await tx.GetSnapshotAsync(workspace.Collection("locations"), cancellationToken);
            var target = locations.Documents.FirstOrDefault(d => d.Id == locationId);
            if (target == null) return Result.Failure(new Error("Location.NotFound", "La sede no existe."));
            var references = await tx.GetSnapshotAsync(workspace.Collection("catalogItems").WhereArrayContains("LocationIds", locationId).Limit(1), cancellationToken);
            var legacyReferences = await tx.GetSnapshotAsync(workspace.Collection("catalogItems").WhereArrayContains("AvailableAtLocations", locationId).Limit(1), cancellationToken);
            if (references.Count > 0 || legacyReferences.Count > 0)
                return Result.Failure(new Error("Location.Conflict", "Reasigna primero los productos y servicios de esta sede, incluidos los inactivos."));
            var hours = await tx.GetSnapshotAsync(workspace.Collection("hours").Document(locationId).Collection("schedule"), cancellationToken);
            var remaining = locations.Documents.Where(d => d.Id != locationId).OrderBy(d => d.Id, StringComparer.Ordinal).ToList();
            var main = remaining.FirstOrDefault(d => d.ConvertTo<FirestoreLocation>().IsMain) ?? remaining.FirstOrDefault();
            foreach (var doc in remaining)
                tx.Update(doc.Reference, new Dictionary<string, object> { ["IsMain"] = doc.Id == main?.Id });
            foreach (var hour in hours.Documents) tx.Delete(hour.Reference);
            tx.Delete(target.Reference);
            tx.Set(workspace, new Dictionary<string, object> { ["businessRevision"] = Guid.NewGuid().ToString() }, SetOptions.MergeAll);
            return Result.Success();
        }, cancellationToken: cancellationToken);
    }

    private static LocationDto Map(DocumentSnapshot doc)
    {
        var data = doc.ConvertTo<FirestoreLocation>();
        return new LocationDto(doc.Id, data.Name, data.Address, data.Reference, data.MapUrl, data.IsMain);
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
