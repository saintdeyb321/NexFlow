namespace NexFlow.Application.Abstractions.Repositories;

public interface ISystemAdministratorRepository
{
    Task<bool> IsUserSuperAdminAsync(Guid userId, CancellationToken cancellationToken);

}