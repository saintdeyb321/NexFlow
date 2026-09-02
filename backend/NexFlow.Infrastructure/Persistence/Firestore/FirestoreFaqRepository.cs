using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreFaqRepository : IFaqRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreFaqRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    public async Task<IEnumerable<FaqDto>> GetFaqsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var query = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("faqs");
        var snapshot = await query.GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Select(doc =>
        {
            var data = doc.ConvertTo<FirestoreFaq>();
            return new FaqDto
            {
                Id = doc.Id,
                Question = data.Question,
                Answer = data.Answer,
                Category = data.Category,
                IsActive = data.IsActive
            };
        });
    }

    public async Task<FaqDto> SaveFaqAsync(Guid workspaceId, FaqDto faq, CancellationToken cancellationToken)
    {
        var collectionRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("faqs");
        var docRef = collectionRef.Document(faq.Id);

        // 🔥 Auditoría (Sprint 3.2): La validación del límite (20 FAQs) ya se hace en el BusinessController. 
        // El repositorio solo guarda.

        var firestoreEntity = new FirestoreFaq
        {
            Question = faq.Question,
            Answer = faq.Answer,
            Category = faq.Category,
            IsActive = faq.IsActive
        };

        await docRef.SetAsync(firestoreEntity, cancellationToken: cancellationToken);

        return faq;
    }

    public async Task DeleteFaqAsync(Guid workspaceId, string faqId, CancellationToken cancellationToken)
    {
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("faqs").Document(faqId);
        await docRef.DeleteAsync(Precondition.None, cancellationToken);
    }

    [FirestoreData]
    private class FirestoreFaq
    {
        [FirestoreProperty] public string Question { get; set; } = string.Empty;
        [FirestoreProperty] public string Answer { get; set; } = string.Empty;
        [FirestoreProperty] public string? Category { get; set; }
        [FirestoreProperty] public bool IsActive { get; set; } = true;
    }
}