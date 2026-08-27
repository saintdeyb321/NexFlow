using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Exceptions; // Para lanzar excepciones de negocio

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreFaqRepository : IFaqRepository
{
    private readonly FirestoreDb _firestoreDb;
    private const int MAX_FAQS_PER_WORKSPACE = 30; // 🛡️ EL LÍMITE FILOSÓFICO

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
        var docSnapshot = await docRef.GetSnapshotAsync(cancellationToken);
        bool isNewRecord = !docSnapshot.Exists;
        if (isNewRecord)
        {
            var countSnapshot = await collectionRef.Count().GetSnapshotAsync(cancellationToken);
            if (countSnapshot.Count >= 20)
            {
                throw new DomainException("Se ha alcanzado el límite máximo de 20 Preguntas Frecuentes por negocio.");
            }
        }
        await docRef.SetAsync(faq, cancellationToken: cancellationToken);
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