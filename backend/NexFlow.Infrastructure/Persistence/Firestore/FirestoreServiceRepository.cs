using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;

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
            return new ServiceDto
            {
                Id = doc.Id,
                Name = data.Name,
                Description = data.Description,
                Category = data.Category,
                DurationInMinutes = data.DurationInMinutes,
                Price = (decimal)data.Price, // Casteamos de double (Firestore) a decimal (C#)
                Currency = data.Currency,
                RequiresReservation = data.RequiresReservation,
                IsActive = data.IsActive,
                AvailableAtLocations = data.AvailableAtLocations ?? new List<string>(),
                Metadata = data.Metadata ?? new Dictionary<string, object>()
            };
        });
    }
    public async Task<ServiceDto> SaveServiceAsync(Guid workspaceId, ServiceDto service, CancellationToken cancellationToken)
    {
        // 1. Si el Frontend no mandó ID, lo generamos.
        var serviceId = string.IsNullOrEmpty(service.Id) ? Guid.NewGuid().ToString() : service.Id;

        // 2. Asignación directa (soluciona el error del 'record')
        service.Id = serviceId;

        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString())
                        .Collection("services").Document(serviceId);

        await docRef.SetAsync(service, cancellationToken: cancellationToken);

        // 3. Retornamos el DTO actualizado con su ID real
        return service;
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
        [FirestoreProperty] public string? Description { get; set; }
        [FirestoreProperty] public string? Category { get; set; }
        [FirestoreProperty] public int DurationInMinutes { get; set; }
        [FirestoreProperty] public double Price { get; set; }
        [FirestoreProperty] public string Currency { get; set; } = "PEN";
        [FirestoreProperty] public bool RequiresReservation { get; set; } = true;
        [FirestoreProperty] public bool IsActive { get; set; } = true;
        [FirestoreProperty] public List<string> AvailableAtLocations { get; set; } = new();
        [FirestoreProperty] public Dictionary<string, object> Metadata { get; set; } = new();
    }
}