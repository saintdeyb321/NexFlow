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

    public async Task SaveFaqAsync(Guid workspaceId, FaqDto faq, CancellationToken cancellationToken)
    {
        var collection = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("faqs");

        // 🛡️ REGLA: Si es una creación nueva (ID vacío o nuevo), validamos que no exceda el límite
        if (string.IsNullOrEmpty(faq.Id) || faq.Id == Guid.Empty.ToString())
        {
            var countQuery = collection.Count();
            var countSnapshot = await countQuery.GetSnapshotAsync(cancellationToken);

            if (countSnapshot.Count >= MAX_FAQS_PER_WORKSPACE)
            {
                throw new DomainException($"Límite alcanzado. El sistema permite un máximo de {MAX_FAQS_PER_WORKSPACE} Preguntas Frecuentes para mantener la eficiencia de la Inteligencia Artificial.");
            }
        }

        var docId = string.IsNullOrEmpty(faq.Id) ? Guid.NewGuid().ToString() : faq.Id;
        var docRef = collection.Document(docId);

        var data = new FirestoreFaq
        {
            Question = faq.Question,
            Answer = faq.Answer,
            Category = faq.Category,
            IsActive = faq.IsActive
        };

        await docRef.SetAsync(data, SetOptions.MergeAll, cancellationToken);
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