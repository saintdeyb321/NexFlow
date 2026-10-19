using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using Grpc.Core;
using NexFlow.Application.Features.Business;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreBusinessProfileRepository : IBusinessProfileRepository
{
    private readonly FirestoreDb _firestoreDb;
    public FirestoreBusinessProfileRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    public async Task<BusinessProfileDto?> GetProfileAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("business").Document("profile");
        DocumentSnapshot snapshot;
        try { snapshot = await docRef.GetSnapshotAsync(cancellationToken); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (RpcException) when (cancellationToken.IsCancellationRequested)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw;
        }
        catch (RpcException ex) when (!cancellationToken.IsCancellationRequested &&
            ex.StatusCode is StatusCode.Unavailable or StatusCode.DeadlineExceeded or StatusCode.ResourceExhausted)
        {
            throw new BusinessProfileUnavailableException(ex.StatusCode == StatusCode.DeadlineExceeded, ex);
        }

        if (!snapshot.Exists) return null;
        var data = snapshot.ConvertTo<FirestoreBusinessProfile>();

        return new BusinessProfileDto(
            data.CommercialName,
            data.TaxId,
            data.ContactEmail,
            data.WhatsAppNumber,
            data.Description,
            data.TimeZone ?? "America/Lima"
        );
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
            Description = profile.Description,
            TimeZone = profile.TimeZone 
        };
        await _firestoreDb.RunTransactionAsync(async tx =>
        {
            await FirestoreCatalogArtifactRepository.InvalidateForMutationAsync(tx, docRef.Parent.Parent!, null, cancellationToken);
            tx.Set(docRef, data, SetOptions.MergeAll);
        }, cancellationToken: cancellationToken);
    }

    [FirestoreData]
    private class FirestoreBusinessProfile
    {
        [FirestoreProperty] public string CommercialName { get; set; } = string.Empty;
        [FirestoreProperty] public string TaxId { get; set; } = string.Empty;
        [FirestoreProperty] public string ContactEmail { get; set; } = string.Empty;
        [FirestoreProperty] public string WhatsAppNumber { get; set; } = string.Empty;
        [FirestoreProperty] public string Description { get; set; } = string.Empty;
        [FirestoreProperty] public string TimeZone { get; set; } = "America/Lima";
    }
}
