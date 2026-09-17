using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;

using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services; // 🔥 Importación requerida
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Identity.GetMe;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Features.SuperAdmin.Licenses;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;
using NexFlow.Application.Features.SuperAdmin.Workspaces;
using NexFlow.Application.Services;

namespace NexFlow.Application.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // 1. Registrar Servicios de Dominio y Motores
        services.AddScoped<IReservationEngine, NexFlow.Application.Engines.Reservation.ReservationEngine>();
        services.AddScoped<IEntitlementService, EntitlementService>();

        // 2. Registrar Handlers de Casos de Uso (Commands/Queries)
        services.AddScoped<ProvisionClientCommandHandler>();
        services.AddScoped<RenewLicenseCommandHandler>();
        services.AddScoped<SuspendClientCommandHandler>();

        // 🔥 Auditoría (Sprint 2.3): El Orquestador y sus nuevos Microservicios
        services.AddScoped<ProcessIncomingMessageCommandHandler>();
        services.AddScoped<IIncomingMessageGuard, IncomingMessageGuard>();
        services.AddScoped<IConversationStateService, ConversationStateService>();
        services.AddScoped<IAiResponseOrchestrator, AiResponseOrchestrator>();
        services.AddScoped<ICapabilityExecutor, CapabilityExecutor>();

        services.AddScoped<AssignModuleToLicenseCommandHandler>();
        services.AddScoped<CreateCustomLicenseCommandHandler>();
        services.AddScoped<GetMeQueryHandler>();
        services.AddScoped<SaveLocationCommandHandler>();
        services.AddScoped<GetSystemWorkspacesQueryHandler>();
        services.AddScoped<ReactivateClientCommandHandler>();
        services.AddScoped<DeleteClientCommandHandler>();

        services.AddScoped<ICatalogHashService, CatalogHashService>();
        services.AddScoped<ICatalogGenerationService, CatalogGenerationService>();

        return services;
    }
}