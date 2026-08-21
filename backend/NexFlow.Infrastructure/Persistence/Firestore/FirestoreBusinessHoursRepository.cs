using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreBusinessHoursRepository : IBusinessHoursRepository
{
    private readonly FirestoreDb _firestoreDb;
    public FirestoreBusinessHoursRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

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

    [FirestoreData]
    private class FirestoreBusinessHours
    {
        [FirestoreProperty] public int DayOfWeek { get; set; }
        [FirestoreProperty] public string OpenTime { get; set; } = string.Empty;
        [FirestoreProperty] public string CloseTime { get; set; } = string.Empty;
        [FirestoreProperty] public bool IsClosed { get; set; }
    }
}