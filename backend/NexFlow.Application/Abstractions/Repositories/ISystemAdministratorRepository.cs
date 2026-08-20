using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.Application.Abstractions.Repositories;

public interface ISystemAdministratorRepository
{
    Task<bool> IsUserSuperAdminAsync(Guid userId, CancellationToken cancellationToken);
}