using NexFlow.Domain.Enums;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Domain.Entities;

public class User : Entity
{
    public Email Email { get; private set; } = null!;
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public UserStatus Status { get; private set; }
    public string? FirebaseUid { get; private set; }

    private User() { }

    public static User Create(Email email, string firstName, string lastName)
    {
        return new User
        {
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            Status = UserStatus.Active
        };
    }
    public void LinkFirebaseAccount(string firebaseUid)
    {
        FirebaseUid = firebaseUid;
    }

    public void Suspend() => Status = UserStatus.Suspended;
    public void Activate() => Status = UserStatus.Active;
    public void Deactivate() => Status = UserStatus.Inactive;
}