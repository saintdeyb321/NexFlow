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
using System;

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

        // 3. Registrar Repositorios de PostgreSQL
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<ISystemAdministratorRepository, SystemAdministratorRepository>();

        // 4. NUEVO: Configurar Redis Cache (Sprint 6)
        services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = configuration.GetConnectionString("Redis");
        });
        services.AddScoped<IConversationCache, RedisConversationCache>();

        // 5. NUEVO: Configurar Firestore (Sprint 5)
        var firebaseProjectId = configuration["Firebase:ProjectId"];
        if (!string.IsNullOrEmpty(firebaseProjectId))
        {
            services.AddSingleton(FirestoreDb.Create(firebaseProjectId));
            services.AddScoped<IBusinessConfigurationRepository, FirestoreBusinessConfigurationRepository>();
        }

        // 6. Registrar IClock
        services.AddSingleton<IClock, SystemClock>();

        // 7. Configurar Motores de Inteligencia
        services.AddHttpClient<IAiProvider, GeminiAiProvider>();
        services.AddScoped<IIntentEngine, IntentEngine>();

        services.AddScoped<IAiRouter, AiRouter>();
        services.AddHttpClient<IMessageGateway, EvolutionMessageGateway>();
        return services;
    }
}

public class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}