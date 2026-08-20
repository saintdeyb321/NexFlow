namespace NexFlow.Application.Abstractions;

public interface ICurrentUser
{
    Guid UserId { get; }
    string Email { get; }
    bool IsAuthenticated { get; }
    
}