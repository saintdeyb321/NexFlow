using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Requests;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreRequestRepository : IRequestRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreRequestRepository(FirestoreDb firestoreDb)
    {
        _firestoreDb = firestoreDb;
    }

    private CollectionReference GetCollection(Guid workspaceId) =>
        _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("requests");

    public async Task CreateRequestAsync(Guid workspaceId, RequestRecord request, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(request.Id);

        var data = new Dictionary<string, object>
        {
            { "Id", request.Id },
            { "ConversationId", request.ConversationId },
            { "ConsumerPhone", request.ConsumerPhone },
            { "Type", request.Type.ToString().ToUpperInvariant() },
            { "Title", request.Title },
            { "Description", request.Description },
            { "Status", request.Status.ToString().ToUpperInvariant() },
            { "AssignedTo", request.AssignedTo ?? "" },
            { "Metadata", request.Metadata ?? new Dictionary<string, object>() },
            { "CreatedAt", request.CreatedAt.ToUniversalTime() },
            { "UpdatedAt", request.UpdatedAt.ToUniversalTime() }
        };

        await docRef.SetAsync(data, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<RequestRecord>> GetRequestsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var snapshot = await GetCollection(workspaceId)
            .OrderByDescending("CreatedAt")
            .Limit(100)
            .GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Where(d => d.Exists).Select(MapToRequestRecord).ToList();
    }

    public async Task<RequestRecord?> GetLatestRequestByPhoneAsync(Guid workspaceId, string phone, CancellationToken cancellationToken)
    {
        var snapshot = await GetCollection(workspaceId)
            .WhereEqualTo("ConsumerPhone", phone)
            .OrderByDescending("CreatedAt")
            .Limit(1)
            .GetSnapshotAsync(cancellationToken);

        var doc = snapshot.Documents.FirstOrDefault();
        return (doc != null && doc.Exists) ? MapToRequestRecord(doc) : null;
    }

    public async Task UpdateRequestStatusAsync(Guid workspaceId, string requestId, string status, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(requestId);
        await docRef.UpdateAsync(new Dictionary<string, object>
        {
            { "Status", status.ToUpperInvariant() },
            { "UpdatedAt", DateTime.UtcNow }
        }, cancellationToken: cancellationToken);
    }

    // 🔥 Método Helper para lectura segura compatible con datos viejos
    private static RequestRecord MapToRequestRecord(DocumentSnapshot doc)
    {
        doc.TryGetValue("Status", out string statusString);
        var statusEnum = Enum.TryParse<RequestStatus>(statusString, true, out var parsedStatus) ? parsedStatus : RequestStatus.Pending;

        doc.TryGetValue("Type", out string typeString);
        var typeEnum = Enum.TryParse<RequestType>(typeString, true, out var parsedType) ? parsedType : RequestType.Other;

        doc.TryGetValue("ConversationId", out string conversationId);
        doc.TryGetValue("AssignedTo", out string assignedTo);
        doc.TryGetValue("Metadata", out Dictionary<string, object> metadata);

        return new RequestRecord
        {
            Id = doc.GetValue<string>("Id"),
            ConversationId = conversationId ?? string.Empty,
            ConsumerPhone = doc.GetValue<string>("ConsumerPhone"),
            Type = typeEnum,
            Title = doc.GetValue<string>("Title"),
            Description = doc.GetValue<string>("Description"),
            Status = statusEnum,
            AssignedTo = assignedTo,
            Metadata = metadata ?? new Dictionary<string, object>(),
            CreatedAt = doc.GetValue<DateTime>("CreatedAt"),
            UpdatedAt = doc.GetValue<DateTime>("UpdatedAt")
        };
    }
}