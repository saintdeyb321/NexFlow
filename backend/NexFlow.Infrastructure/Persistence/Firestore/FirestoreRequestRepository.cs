using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

        // Mapeo manual para no contaminar la capa Application con atributos [FirestoreData]
        var data = new Dictionary<string, object>
        {
            { "Id", request.Id },
            { "ConsumerPhone", request.ConsumerPhone },
            { "Title", request.Title },
            { "Description", request.Description },
            { "Status", request.Status },
            { "CreatedAt", request.CreatedAt.ToUniversalTime() },
            { "UpdatedAt", request.UpdatedAt.ToUniversalTime() }
        };

        await docRef.SetAsync(data, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<RequestRecord>> GetRequestsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var snapshot = await GetCollection(workspaceId).OrderByDescending("CreatedAt").GetSnapshotAsync(cancellationToken);
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