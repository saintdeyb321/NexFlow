using Google.Cloud.Firestore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Infrastructure.Cache;
using NexFlow.Infrastructure.Engines.AI;
using NexFlow.Infrastructure.Gateways;
using NexFlow.Infrastructure.Gateways.Storage;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Infrastructure.Workers;
using StackExchange.Redis;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;

namespace NexFlow.Infrastructure.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Base de Datos Relacional
        services.AddDbContext<NexFlowDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<NexFlowDbContext>());

        // 2. Repositorios (PostgreSQL)
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<ILicenseRepository, LicenseRepository>();
        services.AddScoped<ITemplateRepository, TemplateRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IAuditLogRepository, AuditLogRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<NexFlow.Application.Features.Reservations.IReservationResourceCoordinator, ReservationResourceCoordinator>();
        services.AddScoped<ISystemAdministratorRepository, SystemAdministratorRepository>();
        services.AddScoped<IModuleRepository, ModuleRepository>();

        // 3. Caché
        services.AddStackExchangeRedisCache(options => { options.Configuration = configuration.GetConnectionString("Redis"); });
        services.AddScoped<IConversationCache, RedisConversationCache>();


        // 4. Base de Datos Documental (Firestore)
        var firebaseProjectId = configuration["Firebase:ProjectId"];
        if (!string.IsNullOrEmpty(firebaseProjectId))
        {
            services.AddSingleton(FirestoreDb.Create(firebaseProjectId));
            services.AddScoped<IBusinessProfileRepository, FirestoreBusinessProfileRepository>();
            services.AddScoped<FirestoreLocationRepository>();
            services.AddScoped<ILocationRepository>(p => p.GetRequiredService<FirestoreLocationRepository>());
            services.AddScoped<IBusinessHoursRepository, FirestoreBusinessHoursRepository>();
            services.AddScoped<IFaqRepository, FirestoreFaqRepository>();
            services.AddScoped<FirestoreCatalogRepository>();
            services.AddScoped<ICatalogRepository>(p => p.GetRequiredService<FirestoreCatalogRepository>());
            services.AddScoped<IConsumerIdentityRepository, FirestoreConsumerIdentityRepository>();
            services.AddScoped<IConversationRepository, FirestoreConversationRepository>();
            services.AddScoped<IConversationStateRepository, FirestoreConversationStateRepository>();

            services.AddScoped<IRequestRepository, FirestoreRequestRepository>();
            services.AddScoped<ITenantCleanupService, FirestoreTenantCleanupService>();

            services.AddScoped<ICatalogArtifactRepository, FirestoreCatalogArtifactRepository>();
            services.AddScoped<ICatalogGenerationUsageRepository, FirestoreCatalogArtifactRepository>();
            services.AddScoped<ICatalogUploadRepository, FirestoreCatalogUploadRepository>();
        }

        // 5. Utilidades y Motores de IA
        services.AddSingleton<IClock, SystemClock>();
        services.AddOptions<AssistantRecoveryOptions>().Bind(configuration.GetSection("AssistantRecovery"))
            .Validate(o => o.MaxClarifications is >= 1 and <= 10 && o.MaxInputCharacters is >= 100 and <= 8000
                && o.ClientAiCallsPerMinute is >= 1 and <= 30 && o.WorkspaceAiCallsPerMinute is >= 1 and <= 1000
                && o.ContextExpiryHours is >= 1 and <= 168, "Invalid AssistantRecovery limits.").ValidateOnStart();
        services.AddHttpClient<IAiProvider, GeminiAiProvider>().RedactLoggedHeaders(static _ => true);
        services.AddHttpClient<IAiProvider, GroqAiProvider>().RedactLoggedHeaders(static _ => true);
        services.AddScoped<IAiRouter, AiRouter>();

        services.AddScoped<IOutboxRepository, PostgresOutboxRepository>();
        services.AddHostedService<OutboxProcessorWorker>();
        services.AddHostedService<ReservationResourceRecoveryWorker>();
        services.AddHostedService<CatalogUploadCleanupWorker>();
        services.AddOptions<CatalogGenerationOptions>().Configure(options =>
        {
            var value = configuration["CatalogGeneration:TimeoutMinutes"];
            if (value != null) options.TimeoutMinutes = int.TryParse(value, out var minutes) ? minutes : 0;
        }).Validate(options => options.TimeoutMinutes is >= 1 and <= 30, "CatalogGeneration:TimeoutMinutes debe estar entre 1 y 30.").ValidateOnStart();
        services.AddHostedService<CatalogGenerationRecoveryWorker>();
        services.AddScoped<ITenantDeletionScheduler, TenantDeletionScheduler>();
        services.AddHostedService<TenantDeletionWorker>();

        services.AddScoped<IInboundMessageRepository, InboundMessageRepository>();
        services.AddHostedService<InboundMessageWorker>();

        // 6. Gateways Externos
        services.AddHttpClient<IMessageGateway, EvolutionMessageGateway>().RedactLoggedHeaders(static _ => true);
        services.AddHttpClient<IWorkflowGateway, N8nWorkflowGateway>().RedactLoggedHeaders(static _ => true);
        services.AddScoped<IInstanceResolver, DefaultInstanceResolver>();
        services.AddScoped<IFileStorage, CloudinaryFileStorage>();
        services.AddScoped<IWhatsAppConnectionRepository, WhatsAppConnectionRepository>();
        services.AddHttpClient<IEvolutionConnectionService, EvolutionConnectionService>().RedactLoggedHeaders(static _ => true);

        //notificacion
        services.AddScoped<IOrderRepository, FirestoreOrderRepository>();

        services.AddScoped<INotificationRepository, NotificationRepository>();


        // 7. Conexión a Redis Resiliente
        var redisConnString = configuration.GetConnectionString("Redis") ?? "localhost:6379";
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var options = ConfigurationOptions.Parse(redisConnString);
            options.AbortOnConnectFail = false;
            return ConnectionMultiplexer.Connect(options);
        });

        return services;
    }
}

public class SystemClock : IClock { public DateTime UtcNow => DateTime.UtcNow; }
