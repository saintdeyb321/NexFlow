using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Exceptions; // 🔥 Necesario para la excepción de dominio
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
        var schedule = hours.ToList();
        if (schedule.Any(h => h.DayOfWeek < 0 || h.DayOfWeek > 6) || schedule.Select(h => h.DayOfWeek).Distinct().Count() != schedule.Count)
            throw new DomainException("Los días deben ser únicos y estar entre 0 y 6.");
        foreach (var hour in schedule)
        {
            if (!TimeOnly.TryParseExact(hour.OpenTime, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var open) ||
                !TimeOnly.TryParseExact(hour.CloseTime, "HH:mm", System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out var close))
                throw new DomainException("Las horas deben tener formato HH:mm válido.");
            if (!hour.IsClosed && open >= close)
                throw new DomainException("La apertura debe ser anterior al cierre.");
        }
        if (locationId?.Contains('/') == true) throw new DomainException("ID de sede inválido.");
        var docId = string.IsNullOrEmpty(locationId) ? "global" : locationId;
        var workspace = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString());
        var collectionRef = workspace.Collection("hours").Document(docId).Collection("schedule");
        await _firestoreDb.RunTransactionAsync(async tx =>
        {
            await tx.GetSnapshotAsync(workspace, cancellationToken);
            if (!string.IsNullOrEmpty(locationId))
            {
                var location = await tx.GetSnapshotAsync(workspace.Collection("locations").Document(locationId), cancellationToken);
                if (!location.Exists) throw new KeyNotFoundException("La sede no existe.");
            }
            var previous = await tx.GetSnapshotAsync(collectionRef, cancellationToken);
            var ids = schedule.Select(h => h.DayOfWeek.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToHashSet();
            foreach (var doc in previous.Documents.Where(d => !ids.Contains(d.Id))) tx.Delete(doc.Reference);
            foreach (var hour in schedule)
                tx.Set(collectionRef.Document(hour.DayOfWeek.ToString(System.Globalization.CultureInfo.InvariantCulture)), new FirestoreBusinessHours
                {
                    DayOfWeek = hour.DayOfWeek, OpenTime = hour.OpenTime, CloseTime = hour.CloseTime, IsClosed = hour.IsClosed
                });
            tx.Set(workspace, new Dictionary<string, object> { ["businessRevision"] = Guid.NewGuid().ToString() }, SetOptions.MergeAll);
        }, cancellationToken: cancellationToken);
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
