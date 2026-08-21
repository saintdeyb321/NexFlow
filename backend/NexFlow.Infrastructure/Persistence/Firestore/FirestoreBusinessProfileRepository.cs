using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreBusinessProfileRepository : IBusinessProfileRepository
{
    private readonly FirestoreDb _firestoreDb;
    public FirestoreBusinessProfileRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    public async Task<BusinessProfileDto?> GetProfileAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("business").Document("profile");
        var snapshot = await docRef.GetSnapshotAsync(cancellationToken);

        if (!snapshot.Exists) return null;
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

    [FirestoreData]
    private class FirestoreBusinessProfile
    {
        [FirestoreProperty] public string CommercialName { get; set; } = string.Empty;
        [FirestoreProperty] public string TaxId { get; set; } = string.Empty;
        [FirestoreProperty] public string ContactEmail { get; set; } = string.Empty;
        [FirestoreProperty] public string WhatsAppNumber { get; set; } = string.Empty;
        [FirestoreProperty] public string Description { get; set; } = string.Empty;
    }
}