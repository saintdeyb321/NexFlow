using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(Email email, CancellationToken cancellationToken);
    void Add(User user);
    Task<User?> GetByFirebaseUidAsync(string firebaseUid, CancellationToken cancellationToken);
}