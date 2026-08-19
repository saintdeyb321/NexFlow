namespace NexFlow.Application.Abstractions;

public interface IUnitOfWork
{
    // Confirma la transacción en base de datos.
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}