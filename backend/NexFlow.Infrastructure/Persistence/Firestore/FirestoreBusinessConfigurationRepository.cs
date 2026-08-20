using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreBusinessConfigurationRepository : IBusinessConfigurationRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreBusinessConfigurationRepository(FirestoreDb firestoreDb)
    {
        _firestoreDb = firestoreDb;
    }

    public async Task<BusinessProfileDto?> GetProfileAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("business").Document("profile");
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);

        if (!snapshot.Exists) return null;

        // Mapeo fuertemente tipado
        var data = snapshot.ConvertTo<FirestoreBusinessProfile>();
        return new BusinessProfileDto(data.CommercialName, data.TaxId, data.ContactEmail, data.WhatsAppNumber, data.Description);
    }

    public async Task SaveProfileAsync(Guid workspaceId, BusinessProfileDto profile, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("business").Document("profile");

        var data = new FirestoreBusinessProfile
        {
            CommercialName = profile.CommercialName,
            TaxId = profile.TaxId,
            ContactEmail = profile.ContactEmail,
            WhatsAppNumber = profile.WhatsAppNumber,
            Description = profile.Description
        };

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

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

    public async Task<IEnumerable<BusinessHoursDto>> GetBusinessHoursAsync(Guid workspaceId, string? locationId, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(locationId) ? "global" : locationId;
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("hours").Document(docId).Collection("schedule");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Select(doc =>
        {
            var data = doc.ConvertTo<FirestoreBusinessHours>();
            return new BusinessHoursDto(data.DayOfWeek, data.OpenTime, data.CloseTime, data.IsClosed);
        });
    }

    public async Task SaveBusinessHoursAsync(Guid workspaceId, string? locationId, IEnumerable<BusinessHoursDto> hours, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(locationId) ? "global" : locationId;
        var collectionRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("hours").Document(docId).Collection("schedule");

        var batch = _firestoreDb.StartBatch();

        foreach (var hour in hours)
        {
            var docRef = collectionRef.Document(hour.DayOfWeek.ToString());
            var data = new FirestoreBusinessHours
            {
                DayOfWeek = hour.DayOfWeek,
                OpenTime = hour.OpenTime,
                CloseTime = hour.CloseTime,
                IsClosed = hour.IsClosed
            };
            batch.Set(docRef, data, SetOptions.MergeAll);
        }

        await batch.CommitAsync(cancellationToken);
    }

    // CLASES DE MAPEO ESTRICTO PARA FIRESTORE (Aíslan la lógica de Google Cloud)
    [FirestoreData]
    private class FirestoreBusinessProfile
    {
        [FirestoreProperty] public string CommercialName { get; set; } = string.Empty;
        [FirestoreProperty] public string TaxId { get; set; } = string.Empty;
        [FirestoreProperty] public string ContactEmail { get; set; } = string.Empty;
        [FirestoreProperty] public string WhatsAppNumber { get; set; } = string.Empty;
        [FirestoreProperty] public string Description { get; set; } = string.Empty;
    }

    [FirestoreData]
    private class FirestoreLocation
    {
        [FirestoreProperty] public string Name { get; set; } = string.Empty;
        [FirestoreProperty] public string Address { get; set; } = string.Empty;
        [FirestoreProperty] public string Reference { get; set; } = string.Empty;
        [FirestoreProperty] public bool IsMain { get; set; }
    }

    [FirestoreData]
    private class FirestoreBusinessHours
    {
        [FirestoreProperty] public int DayOfWeek { get; set; }
        [FirestoreProperty] public string OpenTime { get; set; } = string.Empty;
        [FirestoreProperty] public string CloseTime { get; set; } = string.Empty;
        [FirestoreProperty] public bool IsClosed { get; set; }
    }
}