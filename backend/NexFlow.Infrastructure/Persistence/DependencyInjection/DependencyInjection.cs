using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Infrastructure.Cache;
using NexFlow.Infrastructure.Engines.AI;
using NexFlow.Infrastructure.Engines.Intent;
using NexFlow.Infrastructure.Gateways;
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

        // 3. Registrar Repositorios (Aquí conectamos los contratos con la realidad)
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>(); // Descomenta cuando lo crees
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();     // Descomenta cuando lo crees

        // 4. Registrar IClock
        services.AddSingleton<IClock, SystemClock>();

        // 5. Configurar Motores de Inteligencia (Mega-Sprint 3)
        services.AddHttpClient<IAiProvider, GeminiAiProvider>();
        services.AddScoped<IIntentEngine, IntentEngine>();

        services.AddScoped<IReservationRepository, ReservationRepository>();

        // Configurar Redis Cache
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
        });
        services.AddScoped<IConversationCache, RedisConversationCache>();

        services.AddHttpClient<IMessageGateway, EvolutionMessageGateway>();

        return services;
    }
}

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}