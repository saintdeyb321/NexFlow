using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;
using NexFlow.Application.Engines.Dispatcher.Handlers;
using NexFlow.Application.Features.Automation.ProcessMessage;
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
        services.AddScoped<ProcessIncomingMessageCommandHandler>();
        services.AddScoped<AssignModuleToLicenseCommandHandler>();
        services.AddScoped<CreateCustomLicenseCommandHandler>();
        services.AddScoped<GetMeQueryHandler>();

        // 3. Registrar Handlers del Motor Conversacional (Module Dispatcher)
        services.AddScoped<IModuleDispatcher, ModuleDispatcher>();
        services.AddScoped<IModuleHandler, ReservationModuleHandler>();
        services.AddScoped<IModuleHandler, FaqModuleHandler>();
        services.AddScoped<IModuleHandler, CatalogModuleHandler>();
        services.AddScoped<IModuleHandler, ServiceModuleHandler>();
        services.AddScoped<IModuleHandler, RequestModuleHandler>();

        return services;
    }
}