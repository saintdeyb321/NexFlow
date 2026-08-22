using Google.Cloud.Firestore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Engines.Intent;
using NexFlow.Infrastructure.Cache;
using NexFlow.Infrastructure.Engines.AI;
using NexFlow.Infrastructure.Engines.Intent;
using NexFlow.Infrastructure.Gateways;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using StackExchange.Redis;

namespace NexFlow.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Base de Datos Relacional
        services.AddDbContext<NexFlowDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<NexFlowDbContext>());

        // 2. Repositorios
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<ISystemAdministratorRepository, SystemAdministratorRepository>();
        services.AddScoped<IModuleRepository, ModuleRepository>();

        // 3. Caché
        services.AddStackExchangeRedisCache(options => { options.Configuration = configuration.GetConnectionString("Redis"); });
        services.AddScoped<IConversationCache, RedisConversationCache>();

        // 4. Base de Datos Documental
        var firebaseProjectId = configuration["Firebase:ProjectId"];
        if (!string.IsNullOrEmpty(firebaseProjectId))
        {
            services.AddSingleton(FirestoreDb.Create(firebaseProjectId));
            services.AddScoped<IBusinessProfileRepository, FirestoreBusinessProfileRepository>();
            services.AddScoped<ILocationRepository, FirestoreLocationRepository>();
            services.AddScoped<IBusinessHoursRepository, FirestoreBusinessHoursRepository>();
            services.AddScoped<IFaqRepository, FirestoreFaqRepository>();
            services.AddScoped<IServiceRepository, FirestoreServiceRepository>();
        }

        // 5. Utilidades y Motores de IA
        services.AddSingleton<IClock, SystemClock>();
        services.AddHttpClient<IAiProvider, GeminiAiProvider>();
        services.AddScoped<IIntentEngine, IntentEngine>();
        services.AddScoped<IAiRouter, AiRouter>();

        // 6. Gateways Externos (Producción)
        services.AddHttpClient<IMessageGateway, EvolutionMessageGateway>();
        services.AddHttpClient<IWorkflowGateway, N8nWorkflowGateway>();
        // Inyectamos el Resolver temporal
        services.AddScoped<IInstanceResolver, DefaultInstanceResolver>();

        // CORRECCIÓN 2: Conexión a Redis Resiliente y "Perezosa" (Lazy Connection)
        var redisConnString = configuration.GetConnectionString("Redis") ?? "localhost:6379";

        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = ConfigurationOptions.Parse(redisConnString);
            options.AbortOnConnectFail = false; // Evita que la API crashee si Redis tarda en levantar
            return ConnectionMultiplexer.Connect(options);
        });

        return services;
    }
}

public class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }