using System;

namespace NexFlow.Domain.Entities;

public class SystemAdministrator : Entity
{
    public Guid UserId { get; private set; }
    public string GrantedBy { get; private set; } = null!;

    private SystemAdministrator() { }

    public static SystemAdministrator Create(Guid userId, string grantedBy)
    {
        return new SystemAdministrator
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GrantedBy = grantedBy
        };
    }
}