using Microsoft.Extensions.DependencyInjection;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.Dialogue.Policy;
using NexFlow.Application.Features.Automation.Dialogue.Composition;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services; // 🔥 Importación requerida
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.LocationAvailability;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Dashboard;
using NexFlow.Application.Features.Identity.GetMe;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Notifications;
using NexFlow.Application.Features.Requests;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Features.SuperAdmin.Licenses;
using NexFlow.Application.Features.SuperAdmin.ProvisionClient;
using NexFlow.Application.Features.SuperAdmin.Workspaces;
using NexFlow.Application.Services;
using NexFlow.Application.Features.Automation.Agent;

namespace NexFlow.Application.DependencyInjection;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        // 1. Registrar Servicios de Dominio y Motores
        services.AddScoped<NexFlow.Application.Engines.Reservation.ReservationEngine>();
        services.AddSingleton<IAgentDemoPolicy>(p => AgentDemoPolicy.Live(p.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<AgentDemoOptions>>()));
        services.AddOptions<AgentDemoOptions>();
        services.AddScoped<IReservationEngine>(p => new DemoReservationEngine(p.GetRequiredService<NexFlow.Application.Engines.Reservation.ReservationEngine>(), p.GetRequiredService<IAgentDemoPolicy>()));
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
        services.AddScoped<IExistingReservationFlow, ExistingReservationFlow>();
        services.AddScoped<IConversationRecoveryService, ConversationRecoveryService>();
        services.AddScoped<ConversationFocusService>();
        services.AddSingleton<NameSlotResolver>();
        services.AddSingleton<LocationMentionResolver>();
        services.AddSingleton<OfferingMentionResolver>();
        services.AddSingleton<BusinessDateResolver>();
        services.AddSingleton<TimePreferenceResolver>();
        services.AddScoped<IWorkspaceSlotResolver, WorkspaceSlotResolver>();
        services.AddSingleton<DialoguePolicy>();
        services.AddSingleton<OrderMentionResolver>();
        services.AddScoped<ICommerceTurnResolver, CommerceTurnResolver>();
        services.AddScoped<ITurnUnderstandingPipeline, TurnUnderstandingPipeline>();
        services.AddOptions<ResponseCompositionOptions>();
        services.AddScoped<IResponseComposer, ResponseComposer>();

        services.AddScoped<AssignModuleToLicenseCommandHandler>();
        services.AddScoped<CreateCustomLicenseCommandHandler>();
        services.AddScoped<GetMeQueryHandler>();
        services.AddScoped<SaveLocationCommandHandler>();
        services.AddScoped<GetSystemWorkspacesQueryHandler>();
        services.AddScoped<ReactivateClientCommandHandler>();
        services.AddScoped<DeleteClientCommandHandler>();
        services.AddScoped<IKnowledgeService, KnowledgeService>();
        services.AddScoped<IBusinessReadTools, BusinessReadTools>();
        services.AddScoped<IAgentReadTools, AgentReadTools>();
        services.AddScoped<IAgentBookingDrafts, AgentBookingDrafts>();
        services.AddScoped<LlmFirstShadowAgent>();
        services.AddScoped<IExperimentalAgentRuntime, AgentDialogueRuntime>();
        services.AddScoped<IAgentDemoChannel, AgentDemoChannel>();
        services.AddScoped<IConversationalAgent, AgentOwner>();
        services.AddScoped<IOfferingService, OfferingService>();
        services.AddScoped<IAiInterpreter, AiInterpreter>();
        services.AddScoped<ILocationAvailabilityService, LocationAvailabilityService>();

        // 🔥 Registro de los flujos de conversación extraídos del Orchestrator
        services.AddScoped<IBookingFlow, BookingFlow>();
        services.AddScoped<IRequestFlow, RequestFlow>();
        services.AddScoped<ISupportFlow, SupportFlow>();
        services.AddScoped<IChatFlow, ChatFlow>();
        services.AddScoped<IOrderFlow, OrderFlow>();
        services.AddScoped<IContextRecoveryService, ContextRecoveryService>();

        services.AddScoped<ILocationResolverService, LocationResolverService>();
        services.AddScoped<IRequestService, RequestService>();
        services.AddScoped<IHumanHandoffService, HumanHandoffService>();

        services.AddScoped<ICatalogHashService, CatalogHashService>();
        services.AddScoped<ICatalogGenerationService, CatalogGenerationService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IOutboundMessageService, OutboundMessageService>();

        return services;
    }
}

