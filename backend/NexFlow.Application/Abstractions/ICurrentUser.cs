namespace NexFlow.Application.Abstractions;

public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    bool IsAuthenticated { get; }
    // En el futuro, si el SuperAdmin tiene un rol explícito en JWT, se podría agregar:
    // bool IsSuperAdmin { get; }
}