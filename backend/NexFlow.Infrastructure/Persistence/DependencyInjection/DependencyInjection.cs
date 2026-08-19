using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;

namespace NexFlow.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Configurar PostgreSQL
        services.AddDbContext<NexFlowDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        // 2. Registrar IUnitOfWork
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<NexFlowDbContext>());

        // 3. Registrar Repositorios
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        // services.AddScoped<IUserRepository, UserRepository>();
        // services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        // etc...

        // 4. Registrar IClock
        services.AddSingleton<IClock, SystemClock>();

        return services;
    }
}

// Implementación del IClock que definimos en Application
public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}