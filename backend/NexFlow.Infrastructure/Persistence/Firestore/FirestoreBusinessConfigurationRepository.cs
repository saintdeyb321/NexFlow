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

        var data = snapshot.ToDictionary();
        return new BusinessProfileDto(
            data.GetValueOrDefault("CommercialName")?.ToString() ?? string.Empty,
            data.GetValueOrDefault("TaxId")?.ToString() ?? string.Empty,
            data.GetValueOrDefault("ContactEmail")?.ToString() ?? string.Empty,
            data.GetValueOrDefault("WhatsAppNumber")?.ToString() ?? string.Empty,
            data.GetValueOrDefault("Description")?.ToString() ?? string.Empty
        );
    }

    public async Task SaveProfileAsync(Guid workspaceId, BusinessProfileDto profile, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("business").Document("profile");

        var data = new Dictionary<string, object>
        {
            { "CommercialName", profile.CommercialName },
            { "TaxId", profile.TaxId },
            { "ContactEmail", profile.ContactEmail },
            { "WhatsAppNumber", profile.WhatsAppNumber },
            { "Description", profile.Description }
        };

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
    }

    public async Task<IEnumerable<LocationDto>> GetLocationsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("locations");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Select(doc =>
        {
            var data = doc.ToDictionary();
            return new LocationDto(
                doc.Id,
                data.GetValueOrDefault("Name")?.ToString() ?? string.Empty,
                data.GetValueOrDefault("Address")?.ToString() ?? string.Empty,
                data.GetValueOrDefault("Reference")?.ToString() ?? string.Empty,
                data.GetValueOrDefault("IsMain") is bool isMain && isMain
            );
        });
    }

    public async Task SaveLocationAsync(Guid workspaceId, LocationDto location, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(location.Id) ? Guid.NewGuid().ToString() : location.Id;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("locations").Document(docId);

        var data = new Dictionary<string, object>
        {
            { "Name", location.Name },
            { "Address", location.Address },
            { "Reference", location.Reference },
            { "IsMain", location.IsMain }
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
            var data = doc.ToDictionary();
            return new BusinessHoursDto(
                Convert.ToInt32(data.GetValueOrDefault("DayOfWeek") ?? 0),
                data.GetValueOrDefault("OpenTime")?.ToString() ?? "00:00",
                data.GetValueOrDefault("CloseTime")?.ToString() ?? "00:00",
                data.GetValueOrDefault("IsClosed") is bool isClosed && isClosed
            );
        });
    }

    public async Task SaveBusinessHoursAsync(Guid workspaceId, string? locationId, IEnumerable<BusinessHoursDto> hours, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(locationId) ? "global" : locationId;
        var collectionRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("hours").Document(docId).Collection("schedule");

        // Firebase recomienda usar un Batch para guardados múltiples simultáneos
        var batch = _firestoreDb.StartBatch();

        foreach (var hour in hours)
        {
            var docRef = collectionRef.Document(hour.DayOfWeek.ToString());
            var data = new Dictionary<string, object>
            {
                { "DayOfWeek", hour.DayOfWeek },
                { "OpenTime", hour.OpenTime },
                { "CloseTime", hour.CloseTime },
                { "IsClosed", hour.IsClosed }
            };
            batch.Set(docRef, data, SetOptions.MergeAll);
        }

        await batch.CommitAsync(cancellationToken);
    }
}