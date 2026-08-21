using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Knowledge;


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
            return new FaqDto(doc.Id, data.Question, data.Answer, data.Category);
        });
    }

    public async Task SaveFaqAsync(Guid workspaceId, FaqDto faq, CancellationToken cancellationToken)
    {
        var docId = string.IsNullOrEmpty(faq.Id) ? Guid.NewGuid().ToString() : faq.Id;
        var docRef = _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("faqs").Document(docId);

        var data = new FirestoreFaq { Question = faq.Question, Answer = faq.Answer, Category = faq.Category };
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
        [FirestoreProperty] public string Category { get; set; } = string.Empty;
    }
}