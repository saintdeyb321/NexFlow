using Google.Cloud.Firestore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Notifications;

namespace NexFlow.Infrastructure.Persistence.Firestore;

public class FirestoreNotificationRepository : INotificationRepository
{
    private readonly FirestoreDb _firestoreDb;

    public FirestoreNotificationRepository(FirestoreDb firestoreDb) => _firestoreDb = firestoreDb;

    private CollectionReference GetCollection(Guid workspaceId) =>
        _firestoreDb.Collection("workspaces").Document(workspaceId.ToString()).Collection("notifications");

    public async Task CreateAsync(Guid workspaceId, NotificationRecord notification, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(notification.Id);

        var data = new Dictionary<string, object>
        {
            { "Id", notification.Id },
            { "ModuleCode", notification.ModuleCode },
            { "Type", notification.Type.ToString() },
            { "Title", notification.Title },
            { "Message", notification.Message },
            { "IsRead", notification.IsRead },
            { "ActionUrl", notification.ActionUrl ?? "" },
            { "CreatedAt", notification.CreatedAt.ToUniversalTime() }
        };

        await docRef.SetAsync(data, cancellationToken: cancellationToken);
    }

    public async Task<IEnumerable<NotificationRecord>> GetUnreadAsync(Guid workspaceId, int limit, CancellationToken cancellationToken)
    {
        var snapshot = await GetCollection(workspaceId)
            .WhereEqualTo("IsRead", false)
            .OrderByDescending("CreatedAt")
            .Limit(limit)
            .GetSnapshotAsync(cancellationToken);

        return snapshot.Documents.Select(doc => new NotificationRecord
        {
            Id = doc.GetValue<string>("Id"),
            ModuleCode = doc.GetValue<string>("ModuleCode"),
            Type = Enum.TryParse<NotificationType>(doc.GetValue<string>("Type"), out var type) ? type : NotificationType.SystemAlert,
            Title = doc.GetValue<string>("Title"),
            Message = doc.GetValue<string>("Message"),
            IsRead = doc.GetValue<bool>("IsRead"),
            ActionUrl = doc.TryGetValue("ActionUrl", out string url) && !string.IsNullOrEmpty(url) ? url : null,
            CreatedAt = doc.GetValue<DateTime>("CreatedAt")
        }).ToList();
    }

    public async Task MarkAsReadAsync(Guid workspaceId, string notificationId, CancellationToken cancellationToken)
    {
        var docRef = GetCollection(workspaceId).Document(notificationId);
        await docRef.UpdateAsync(new Dictionary<string, object> { { "IsRead", true } }, cancellationToken: cancellationToken);
    }
}