using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreServiceRepository : IServiceRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreServiceRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    public async Task<IEnumerable<ServiceDto>> GetServicesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("services");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToDto);
    }

    // 🔥 Auditoría (Fase 3): Consultas específicas y filtradas directamente en Firestore[cite: 2].
    public async Task<IEnumerable<ServiceDto>> GetActiveServicesAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("services")
            .WhereEqualTo("IsActive", true);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToDto);
    }

    public async Task<ServiceDto?> GetServiceByIdAsync(Guid workspaceId, string serviceId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(serviceId)) return null;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("services").Document(serviceId);
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);

        return snapshot.Exists ? MapToDto(snapshot) : null;
    }

    public async Task<IEnumerable<ServiceDto>> GetServicesByCategoryAsync(Guid workspaceId, string category, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("services")
            .WhereEqualTo("IsActive", true)
            .WhereEqualTo("Category", category);
        var snapshot = await query.GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToDto);
    }

    public async Task<ServiceDto> SaveServiceAsync(Guid workspaceId, ServiceDto service, CancellationToken cancellationToken)
    {
        if (service.PriceMinorUnits < 0)
            throw new DomainException("El precio del servicio no puede ser negativo.");

        if (service.RequiresReservation && service.DurationInMinutes < 5)
            throw new DomainException("Los servicios que requieren reserva deben tener una duración mínima de 5 minutos.");

        if (service.AvailableAtLocations != null && service.AvailableAtLocations.Any())
        {
            var locationsSnapshot = await _firestoreDb.Collection("workspaces")
                .Document(workspaceId.ToString())
                .Collection("locations")
                .GetSnapshotAsync(cancellationToken);

            var activeLocationIds = locationsSnapshot.Documents.Select(d => d.Id).ToHashSet();

            foreach (var locId in service.AvailableAtLocations)
            {
                if (!activeLocationIds.Contains(locId))
                    throw new DomainException($"Operación rechazada: La sede con ID {locId} no existe en la base de datos.");
            }
        }

        var collection = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("services");
        var docRef = string.IsNullOrEmpty(service.Id) ? collection.Document() : collection.Document(service.Id);

        service.Id = docRef.Id;

        var firestoreService = new FirestoreService
        {
            Name = service.Name,
            Description = service.Description,
            Category = service.Category,
            DurationInMinutes = service.DurationInMinutes,
            PriceMinorUnits = service.PriceMinorUnits,
            Currency = service.Currency ?? "PEN",
            RequiresReservation = service.RequiresReservation,
            IsActive = service.IsActive,
            AvailableAtLocations = service.AvailableAtLocations ?? new List<string>(),
            Metadata = service.Metadata ?? new Dictionary<string, object>()
        };

        await docRef.SetAsync(firestoreService, SetOptions.MergeAll, cancellationToken);
        return service;
    }

    public async Task DeleteServiceAsync(Guid workspaceId, string serviceId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("services").Document(serviceId);
        await docRef.DeleteAsync(Precondition.None, cancellationToken);
    }

    private static ServiceDto MapToDto(DocumentSnapshot doc)
    {
        var data = doc.ConvertTo<FirestoreService>();
        return new ServiceDto
        {
            Id = doc.Id,
            Name = data.Name,
            Description = data.Description,
            Category = data.Category,
            DurationInMinutes = data.DurationInMinutes,
            PriceMinorUnits = data.PriceMinorUnits,
            Currency = data.Currency,
            RequiresReservation = data.RequiresReservation,
            IsActive = data.IsActive,
            AvailableAtLocations = data.AvailableAtLocations ?? new List<string>(),
            Metadata = data.Metadata ?? new Dictionary<string, object>()
        };
    }

    [FirestoreData]
    private class FirestoreService
    {
        [FirestoreProperty] public string Name { get; set; } = string.Empty;
        [FirestoreProperty] public string? Description { get; set; }
        [FirestoreProperty] public string? Category { get; set; }
        [FirestoreProperty] public int DurationInMinutes { get; set; }
        [FirestoreProperty] public long PriceMinorUnits { get; set; }
        [FirestoreProperty] public string Currency { get; set; } = "PEN";
        [FirestoreProperty] public bool RequiresReservation { get; set; } = true;
        [FirestoreProperty] public bool IsActive { get; set; } = true;
        [FirestoreProperty] public List<string> AvailableAtLocations { get; set; } = new();
        [FirestoreProperty] public Dictionary<string, object> Metadata { get; set; } = new();
    }
}