using NexFlow.Domain.Enums;

namespace NexFlow.Domain.Entities;

public class Membership : Entity
{
    public Guid UserId { get; private set; }
    public Guid WorkspaceId { get; private set; }
    public MembershipRole Role { get; private set; }

    private Membership() { }

    public static Membership Create(Guid userId, Guid workspaceId, MembershipRole role)
    {
        return new Membership
        {
            UserId = userId,
            WorkspaceId = workspaceId,
            Role = role
        };
    }

    public void ChangeRole(MembershipRole newRole)
    {
        Role = newRole;
        UpdateTimestamp();
    }
}