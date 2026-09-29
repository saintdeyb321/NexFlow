using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Requests;
using System.Security.Cryptography;
using System.Text;
using Grpc.Core;

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

    public async Task<(RequestRecord Request, bool Created)> CreateRequestAsync(Guid workspaceId, RequestRecord request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.SourceMessageId))
        {
            var existing = await GetBySourceMessageIdAsync(workspaceId, request.SourceMessageId, cancellationToken);
            if (existing != null) return (existing, false);
            request.Id = SourceDocumentId(request.SourceMessageId);
        }
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
            { "SourceMessageId", request.SourceMessageId ?? "" },
            { "Metadata", request.Metadata ?? new Dictionary<string, object>() },
            { "CreatedAt", request.CreatedAt.ToUniversalTime() },
            { "UpdatedAt", request.UpdatedAt.ToUniversalTime() }
        };

        try { await docRef.CreateAsync(data, cancellationToken); }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.AlreadyExists && !string.IsNullOrWhiteSpace(request.SourceMessageId))
        {
            var existing = await GetByIdAsync(workspaceId, request.Id, cancellationToken);
            if (existing == null || existing.SourceMessageId != request.SourceMessageId) throw;
            return (existing, false);
        }
        return (request, true);
    }

    private static string SourceDocumentId(string sourceMessageId) =>
        "message_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceMessageId)));

    public async Task<RequestRecord?> GetByIdAsync(Guid workspaceId, string requestId, CancellationToken cancellationToken)
    {
        var doc = await GetCollection(workspaceId).Document(requestId).GetSnapshotAsync(cancellationToken);
        return doc.Exists ? MapToRequestRecord(doc) : null;
    }

    public async Task<RequestRecord?> GetBySourceMessageIdAsync(Guid workspaceId, string sourceMessageId, CancellationToken cancellationToken)
    {
        var existing = await GetByIdAsync(workspaceId, SourceDocumentId(sourceMessageId), cancellationToken);
        if (existing != null) return existing;
        var snapshot = await GetCollection(workspaceId).WhereEqualTo("SourceMessageId", sourceMessageId)
            .Limit(1).GetSnapshotAsync(cancellationToken);
        return snapshot.Documents.Select(MapToRequestRecord).FirstOrDefault();
    }

    // 🔥 SPRINT 11 (Auditoría): Firma modificada para soportar Paginación y Filtrado Real
    public async Task<IEnumerable<RequestRecord>> GetRequestsAsync(Guid workspaceId, int limit, string? status, CancellationToken cancellationToken)
    {
        var query = GetCollection(workspaceId).OrderByDescending("CreatedAt").Limit(limit);

        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.WhereEqualTo("Status", status.ToUpperInvariant());
        }

        var snapshot = await query.GetSnapshotAsync(cancellationToken);
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
        if (!Enum.TryParse<RequestStatus>(status, true, out var next) || !Enum.IsDefined(next))
            throw new ArgumentException("Estado inválido.");
        var docRef = GetCollection(workspaceId).Document(requestId);
        await _firestoreDb.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(docRef, cancellationToken);
            if (!snapshot.Exists) throw new KeyNotFoundException("Solicitud no encontrada.");
            if (!RequestRecord.CanTransition(MapToRequestRecord(snapshot).Status, next))
                throw new InvalidOperationException("Transición de estado no permitida.");
            transaction.Update(docRef, new Dictionary<string, object>
            {
                { "Status", next.ToString().ToUpperInvariant() },
                { "UpdatedAt", DateTime.UtcNow }
            });
        }, cancellationToken: cancellationToken);
    }

    // 🔥 SPRINT 11 (Auditoría): Nuevo método para asignar encargados
    public async Task AssignRequestAsync(Guid workspaceId, string requestId, string assignedTo, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(requestId);
        await _firestoreDb.RunTransactionAsync(async transaction =>
        {
            var snapshot = await transaction.GetSnapshotAsync(docRef, cancellationToken);
            if (!snapshot.Exists) throw new KeyNotFoundException("Solicitud no encontrada.");
            transaction.Update(docRef, new Dictionary<string, object>
            {
                { "AssignedTo", assignedTo },
                { "UpdatedAt", DateTime.UtcNow }
            });
        }, cancellationToken: cancellationToken);
    }

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
            SourceMessageId = doc.TryGetValue("SourceMessageId", out string sourceMessageId) ? sourceMessageId : null,
            Metadata = metadata ?? new Dictionary<string, object>(),
            CreatedAt = doc.GetValue<DateTime>("CreatedAt"),
            UpdatedAt = doc.GetValue<DateTime>("UpdatedAt")
        };
    }
}
