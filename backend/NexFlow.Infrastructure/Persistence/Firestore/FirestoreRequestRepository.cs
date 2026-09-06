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
            { "ConsumerPhone", request.ConsumerPhone },
            { "Title", request.Title },
            { "Description", request.Description },
            { "Status", request.Status.ToString() }, // Se guarda como string para mayor claridad en BD
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

        var list = new List<RequestRecord>();

        foreach (var doc in snapshot.Documents)
        {
            if (doc.Exists)
            {
                var statusString = doc.GetValue<string>("Status");
                var statusEnum = Enum.TryParse<RequestStatus>(statusString, true, out var parsed)
                                 ? parsed
                                 : RequestStatus.Pending;

                list.Add(new RequestRecord
                {
                    Id = doc.GetValue<string>("Id"),
                    ConsumerPhone = doc.GetValue<string>("ConsumerPhone"),
                    Title = doc.GetValue<string>("Title"),
                    Description = doc.GetValue<string>("Description"),
                    Status = statusEnum,
                    CreatedAt = doc.GetValue<DateTime>("CreatedAt"),
                    UpdatedAt = doc.GetValue<DateTime>("UpdatedAt")
                });
            }
        }
        return list;
    }

    // 🔥 SPRINT 4: Obtener la última solicitud del cliente
    public async Task<RequestRecord?> GetLatestRequestByPhoneAsync(Guid workspaceId, string phone, CancellationToken cancellationToken)
    {
        var snapshot = await GetCollection(workspaceId)
            .WhereEqualTo("ConsumerPhone", phone)
            .OrderByDescending("CreatedAt")
            .Limit(1)
            .GetSnapshotAsync(cancellationToken);

        var doc = snapshot.Documents.FirstOrDefault();

        if (doc != null && doc.Exists)
        {
            var statusString = doc.GetValue<string>("Status");
            var statusEnum = Enum.TryParse<RequestStatus>(statusString, true, out var parsed)
                             ? parsed
                             : RequestStatus.Pending;

            return new RequestRecord
            {
                Id = doc.GetValue<string>("Id"),
                ConsumerPhone = doc.GetValue<string>("ConsumerPhone"),
                Title = doc.GetValue<string>("Title"),
                Description = doc.GetValue<string>("Description"),
                Status = statusEnum,
                CreatedAt = doc.GetValue<DateTime>("CreatedAt"),
                UpdatedAt = doc.GetValue<DateTime>("UpdatedAt")
            };
        }

        return null;
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
}