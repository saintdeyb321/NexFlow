using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Domain.Entities;
using NexFlow.Domain.ValueObjects;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

public class UserRepository : IUserRepository
{
    private readonly NexFlowDbContext _context;

    public UserRepository(NexFlowDbContext context) => _context = context;

    public void Add(User user) => _context.Users.Add(user);

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken)
    {
        // Instanciamos el ValueObject internamente para que EF Core sepa cómo buscarlo
        var emailVo = new Email(email);
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == emailVo, cancellationToken);
    }

    public async Task<User?> GetByFirebaseUidAsync(string firebaseUid, CancellationToken cancellationToken)
    {
        return await _context.Users.FirstOrDefaultAsync(u => u.FirebaseUid == firebaseUid, cancellationToken);
    }
}