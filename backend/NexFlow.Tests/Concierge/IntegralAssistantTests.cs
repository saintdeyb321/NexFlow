using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

// Handler tests use the existing synthetic stores; interpreter tests execute the
// real parser/validator with intercepted router output, never a live model.
public sealed class IntegralAssistantTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData("RESERVATION")]
    [InlineData("ORDER")]
    public async Task Acknowledgement_does_not_commit_a_review_but_explicit_consent_remains_idempotent(string goal)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true)
            { Now = new(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc) };
        if (goal == "RESERVATION")
        {
            await f.SeedDraft(goal); var draft = await f.Context(); draft.TargetDate = "2026-10-28"; draft.TargetTime = "16:00";
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, draft, default);
            await f.HandleTurn("continuemos", "review");
        }
        else
        {
            await f.HandleTurn("Quiero hacer un pedido", "start");
            await f.HandleTurn("dos panes integrales y un queque", "items");
            await f.HandleTurn("Concepcion", "site");
            await f.HandleTurn("finalizar", "review");
        }
        var state = await f.Context(); var question = state.CurrentQuestion!.QuestionId;
        Assert.Equal(goal == "ORDER" ? ExpectedReply.ConfirmOrder : ExpectedReply.ConfirmReservation, state.CurrentQuestion.Expected);
        var before = AssistantConversationTests.Fixture.BusinessDraft(state);
        await f.HandleTurn("de acuerdo", "ack");
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Equal(question, (await f.Context()).CurrentQuestion!.QuestionId);
        Assert.Contains("sin confirmar", f.Sent["ack"].Content);
        await f.HandleTurn("Confirmar", "consent"); await f.HandleTurn("Confirmar", "consent");
        Assert.Equal(1, f.Bookings.Count + f.Orders.Count);
        if (goal == "ORDER") Assert.Equal(OrderStatus.PendingReview, Assert.Single(f.Orders.Values).Status);
    }

    [Fact]
    public async Task Typed_edit_of_current_field_is_not_erased_by_a_conflicting_location_intent()
    {
        var (interpreter, turn, _) = Interpreter("""{"Intent":"Location","Directive":"ChangeLocation","Act":"Correction","Evidence":[{"Slot":"Act","Text":"La sede","NormalizedValue":"Correction"}]}""");
        var result = await interpreter.InterpretAsync("La sede", turn, default);
        Assert.Equal(ConversationIntent.Reservation, result.Intent);
        Assert.Equal(ConversationDirective.ChangeLocation, result.Directive);
        Assert.False(result.Confirmed); Assert.Null(result.BookingProposal);
    }

    [Fact]
    public async Task Broad_catalog_with_legacy_show_options_stays_read_only()
    {
        var (interpreter, turn, _) = Interpreter("""{"Intent":"ServiceQuery","QueryKind":"Broad","Directive":"ShowOptions"}""");
        var result = await interpreter.InterpretAsync("¿Qué otros servicios ofrecen?", turn, default);
        Assert.Equal(ConversationIntent.ServiceQuery, result.Intent);
        Assert.Equal(ConversationDirective.None, result.Directive); Assert.Null(result.BookingProposal);
    }

    [Fact]
    public async Task Human_assistance_keeps_precedence_over_a_field_edit_during_review()
    {
        const string text = "Que un asesor cambie la sede";
        var (interpreter, turn, _) = Interpreter("""{"Intent":"Support","Directive":"ChangeLocation","Act":"Correction","Evidence":[{"Slot":"Act","Text":"Que un asesor cambie la sede","NormalizedValue":"Correction"}]}""");
        var result = await interpreter.InterpretAsync(text, turn, default);
        Assert.Equal(ConversationIntent.Support, result.Intent); Assert.Equal(ConversationDirective.None, result.Directive);
        Assert.Null(result.BookingProposal); Assert.False(result.Confirmed);
    }

    [Fact]
    public async Task Acknowledgement_is_not_a_request_description_after_separating_consent()
    {
        var f = new AssistantConversationTests.Fixture(); var state = await f.Context();
        state.PendingRecoveryAction = "REQUEST_DETAILS";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await f.HandleTurn("de acuerdo", "request-ack");
        Assert.Contains("Describe brevemente", f.Sent["request-ack"].Content);
        Assert.Equal("REQUEST_DETAILS", (await f.Context()).PendingRecoveryAction);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Interpreter_context_excludes_unlicensed_hints_and_private_draft_fields()
    {
        var (interpreter, turn, router) = Interpreter("""{"Intent":"ServiceQuery","QueryKind":"Broad"}""");
        string? prompt = null;
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback<AiTaskType, string, string, bool, CancellationToken>((_, value, _, _, _) => prompt = value)
            .ReturnsAsync("""{"Intent":"ServiceQuery","QueryKind":"Broad"}""");
        turn = turn with { EnabledModules = new HashSet<string> { "SERVICES" }, CurrentGoal = null,
            LocationHints = ["PRIVATE-LOCATION"], ProductHints = ["PRIVATE-PRODUCT"], ServiceHints = ["Servicio autorizado"],
            DraftItems = ["PRIVATE-ORDER"], LastQuestion = "PRIVATE-HISTORY", AvailabilityReference = new(turn.WorkspaceId, "private-site", "private-service", "2026-10-28", "16:00", turn.LocalBusinessNow.UtcDateTime) };
        await interpreter.InterpretAsync("¿Qué servicios ofrecen?", turn, default);
        foreach (var privateValue in new[] { "PRIVATE-LOCATION", "PRIVATE-PRODUCT", "PRIVATE-ORDER", "PRIVATE-HISTORY", "private-site", "16:00" })
            Assert.DoesNotContain(privateValue, prompt);
        Assert.Contains("Servicio autorizado", prompt);
        output.WriteLine("PromptCharacters=" + prompt!.Length);
    }

    [Theory]
    [InlineData("SERVICES,RESERVATIONS", ConversationIntent.ServiceQuery, KnowledgeTopic.Services, "SERVICES")]
    [InlineData("CATALOG,ORDERS", ConversationIntent.ProductQuery, KnowledgeTopic.Products, "CATALOG")]
    [InlineData("FAQ", ConversationIntent.Faq, KnowledgeTopic.Faqs, "FAQ")]
    [InlineData("CATALOG,SERVICES,RESERVATIONS,ORDERS,FAQ,LOCATIONS,BUSINESS_HOURS,REQUESTS", ConversationIntent.Location, KnowledgeTopic.Locations, "LOCATIONS")]
    public async Task Semantic_read_manifest_and_executor_agree_and_revocation_stops_repository_access(string modules,
        ConversationIntent intent, KnowledgeTopic topic, string revoked)
    {
        var f = new AssistantConversationTests.Fixture(); f.Modules.Clear(); f.Modules.UnionWith(modules.Split(','));
        var manifest = BusinessReadCapabilities.ForModules(f.Modules);
        Assert.Contains(manifest, c => c.Intent == intent && c.Topic == topic);
        Assert.Null(BusinessReadCapabilities.FromIntent(ConversationIntent.Order));
        Assert.Null(BusinessReadCapabilities.FromIntent(ConversationIntent.Reservation));
        Assert.Null(BusinessReadCapabilities.FromIntent(ConversationIntent.Request));
        Assert.Equal(f.Modules.Contains("RESERVATIONS") && f.Modules.Contains("SERVICES"), BusinessReadCapabilities.CanReadAvailability(f.Modules));
        var tools = new BusinessReadTools(f.KnowledgeReads.Object, f.EntitlementReads.Object);
        var request = BusinessReadCapabilities.FromIntent(intent)!;
        var turn = tools.Open(f.Workspace);
        var result = await tools.ReadAsync(turn, request, default);
        Assert.Equal(BusinessReadStatus.Found, result.Status); Assert.Equal(f.Workspace, result.WorkspaceId);
        var calls = f.FactualQueries.Count;
        f.Modules.Remove(revoked);
        Assert.DoesNotContain(BusinessReadCapabilities.ForModules(f.Modules), c => c.Intent == intent);
        Assert.Equal(BusinessReadStatus.Denied, (await tools.ReadAsync(turn, request, default)).Status);
        Assert.Equal(calls, f.FactualQueries.Count); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    private static (AiInterpreter Interpreter, DialogueTurnContext Turn, Mock<IAiRouter> Router) Interpreter(string json)
    {
        var workspace = Guid.NewGuid(); var now = new DateTime(2026, 10, 27, 13, 0, 0, DateTimeKind.Utc);
        var state = new ConversationContextDto { CurrentGoal = "RESERVATION", CurrentStep = "CONFIRM" };
        ConversationQuestion.Remember(workspace, "edit", state, ExpectedReply.EditReservationField, ConversationIntent.Reservation, "Private summary", now);
        var turn = DialogueTurnContext.Capture(workspace, "inbound", state, new(now), TimeZoneInfo.FindSystemTimeZoneById("America/Lima"), ["SERVICES", "RESERVATIONS"]);
        var router = new Mock<IAiRouter>(MockBehavior.Strict);
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(json);
        return (new(router.Object, Mock.Of<IClock>(c => c.UtcNow == now), Mock.Of<IBusinessProfileRepository>(MockBehavior.Strict), NullLogger<AiInterpreter>.Instance), turn, router);
    }
}
