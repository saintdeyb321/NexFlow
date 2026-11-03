using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Domain.Enums;

namespace NexFlow.Tests.Concierge;

// Shared evaluation fixtures; JSON scripts test policy, never real NLU accuracy.
internal sealed class AgentDialogueFixture
{
    public AssistantConversationTests.Fixture Business { get; }
    public Mock<IAiRouter> Router { get; } = new(MockBehavior.Strict);
    public Mock<IConversationRepository> Conversations { get; } = new(MockBehavior.Strict);
    public List<MessageRecord> History { get; } = [];
    public Queue<Func<JsonElement, AgentProposal>> Script { get; } = new();
    public List<string> Inputs { get; } = [];
    public AgentDialogueOptions Options { get; } = new() { Enabled = true };
    public AgentDialogueRuntime Runtime { get; private set; } = null!;
    public LlmFirstShadowAgent Shadow { get; private set; } = null!;
    public FirestoreAgentTurnJournal Journal { get; private set; } = null!;
    public Func<Task>? DuringModel { get; set; }
    public IClock Clock { get; private set; } = null!;
    public AgentShadowOptions ShadowOptions { get; } = new();
    private readonly IAiRouter? _external;

    public AgentDialogueFixture(IAiRouter? external = null)
    {
        _external = external;
        Business = new(conciergeCatalog: true, intelligentCommerce: true, experimentFactory: Configure);
        Business.Now = new(2026, 11, 3, 15, 0, 0, DateTimeKind.Utc);
        Business.Services[0].Name = "Evaluación odontológica"; Business.Services[0].PriceMinorUnits = 6000;
        Business.Services[1].Name = "Extracción dental"; Business.Services[1].PriceMinorUnits = 9000;
        Business.OfferingReads.Setup(o => o.GetServicesAsync(Business.Workspace, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string? location, string? search, CancellationToken _) => Business.Services.Where(s => s.IsActive
                && (location == null || s.LocationScope == "ALL" || s.LocationIds.Contains(location))
                && (string.IsNullOrWhiteSpace(search) || s.Name.Contains(search, StringComparison.OrdinalIgnoreCase))).ToArray());
        Business.UseCatalogKnowledge();
        Business.KnowledgeReads.Setup(k => k.QueryAsync(Business.Workspace, It.IsAny<BusinessKnowledgeSnapshot>(),
            It.Is<KnowledgeQuery>(q => q.Topic == KnowledgeTopic.Locations), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResult { Status = KnowledgeStatus.Found, Source = KnowledgeTopic.Locations,
                Facts = "Sedes sintéticas", References = [new("sede-a", "Concepción"), new("sede-b", "San Jerónimo")] });
        Business.KnowledgeReads.Setup(k => k.QueryAsync(Business.Workspace, It.IsAny<BusinessKnowledgeSnapshot>(),
            It.Is<KnowledgeQuery>(q => q.Topic == KnowledgeTopic.Profile), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResult { Status = KnowledgeStatus.Found, Source = KnowledgeTopic.Profile,
                Facts = "Clínica sintética", Profile = new("Clínica sintética", "Ficticia", "", "", "America/Lima") });
    }
    private IExperimentalAgentRuntime Configure(AssistantConversationTests.Fixture f)
    {
        Options.SyntheticWorkspaceId = f.Workspace;
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => f.Now); Clock = clock.Object;
        Conversations.Setup(c => c.GetConversationAsync(f.Workspace, "chat", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConversationRecord { Id = "chat", ConsumerPhone = AssistantConversationTests.Fixture.Phone, Mode = ConversationMode.Automatic });
        Conversations.Setup(c => c.HasUnconfirmedOutboundAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        Conversations.Setup(c => c.GetMessagesAsync(f.Workspace, "chat", 6, It.IsAny<CancellationToken>(), null, null)).ReturnsAsync(() => History.TakeLast(6).ToArray());
        Router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns(async (AiTaskType task, string system, string input, bool jsonMode, CancellationToken token) => {
                Inputs.Add(input); if (DuringModel != null) await DuringModel();
                if (_external != null) return await _external.ExecuteTaskAsync(task, system, input, jsonMode, token);
                using var json = JsonDocument.Parse(input);
                return JsonSerializer.Serialize(Script.Dequeue()(json.RootElement), new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
            });
        Shadow = new(Router.Object, new AgentReadTools(new BusinessReadTools(f.KnowledgeReads.Object, f.EntitlementReads.Object)),
            f.Contexts, Conversations.Object, f.EntitlementReads.Object, f.Recovery, Clock, Microsoft.Extensions.Options.Options.Create(ShadowOptions),
            new FirestoreAgentEvaluationClaims(f.Firestore.Db, Clock));
        Journal = new(f.Firestore.Db, Clock);
        Runtime = new(Shadow, Journal, f.Contexts, Conversations.Object, f.EntitlementReads.Object, Clock, Microsoft.Extensions.Options.Options.Create(Options),
            new AgentBookingDrafts(new NexFlow.Application.Features.Automation.Dialogue.Understanding.WorkspaceSlotResolver(f.EntitlementReads.Object,
                f.ProfileReads.Object, f.LocationReads.Object, f.OfferingReads.Object, Clock, new(), new(), new(), new(), new(),
                NullLogger<NexFlow.Application.Features.Automation.Dialogue.Understanding.WorkspaceSlotResolver>.Instance),
                f.OfferingReads.Object, f.EntitlementReads.Object, new BusinessReadTools(f.KnowledgeReads.Object, f.EntitlementReads.Object, f.EngineReads.Object), Clock));
        return Runtime;
    }
    public AgentTurnRequest Request(string text, string source) => new(Business.Workspace, "chat", AssistantConversationTests.Fixture.Phone, source, text);
    public void Plan(string text, AgentDecision decision, AgentReadKind? read = null, string? search = null, AgentConversationPlan? conversation = null)
    {
        AgentProposal Response(JsonElement input, bool withTool) => new(decision, text, "Propuesta del modelo, jamás se publica directamente.",
            decision is AgentDecision.ProposeDiscardDraft or AgentDecision.RetainDraft
                && input.GetProperty("Context").TryGetProperty("Question", out var q) && q.ValueKind == JsonValueKind.Object ? q.GetProperty("Id").GetString() : null,
            withTool ? [new(read!.Value, search)] : [], withTool ? [] : read.HasValue
                && input.GetProperty("Reads")[0].GetProperty("Status").GetInt32() == (int)BusinessReadStatus.Found ? [0] : [], new(AgentOpening.None), conversation);
        Script.Enqueue(input => Response(input, read.HasValue));
        if (read.HasValue) Script.Enqueue(input => Response(input, false));
    }
    public async Task<AgentTurnResult> Turn(string text, string source, bool scope = true)
    {
        using var evaluation = scope ? AgentExperimentScope.Begin(Business.Workspace) : null;
        await Business.HandleTurn(text, source);
        var result = evaluation?.Results.SingleOrDefault() ?? AgentTurnResult.Rejected("NoExperimentalResponse");
        History.Add(new() { Sender = SenderType.Consumer, ExternalMessageId = source, Content = text, Timestamp = Business.Now });
        if (result.Publishable) History.Add(new() { Sender = SenderType.AI, Content = result.VerifiedText!, Timestamp = Business.Now });
        return result;
    }
}
