using System;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Domain.Entities;

namespace NexFlow.Application.Abstractions.Repositories;

public interface IUserRepository
{
    void Add(User user);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    // CORREGIDO: Ahora exige un string, no un ValueObject
    Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<User?> GetByFirebaseUidAsync(string firebaseUid, CancellationToken cancellationToken);
}