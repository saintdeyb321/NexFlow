using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;
using System.Text.Json;
using System.Security.Cryptography;
using Moq;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Composition;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Requests;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;

namespace NexFlow.Tests.Concierge;

// Controlled interpretations exercise actual handler/state/flows, not provider NLU.
public sealed class NaturalExperienceTests(ITestOutputHelper output)
{
    private readonly WhatsAppConversationalRepairTests _driver = new(output);
    private static AssistantConversationTests.Fixture Create(bool cta = false)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true, contextualFocus: cta)
            { Now = new(2026, 11, 2, 14, 0, 0, DateTimeKind.Utc) };
        f.Services[1].Name = "Evaluación odontológica"; f.Services[1].PriceMinorUnits = 6000;
        f.UseCatalogKnowledge(); return f;
    }
    private Task Say(AssistantConversationTests.Fixture f, string text, string id, AiInterpretation? ai = null)
        => _driver.Speak(f, text, id, ai);
    private static AiInterpretation Act(string text, ConversationIntent intent, DialogueAct act) => new() {
        Intent = intent, Act = act, Evidence = [new("Act", text, act.ToString())]
    };
    public sealed record EvaluationCase(string Id, string Kind, string Text, string? Fragment);
    private static byte[] EvaluationBytes()
    {
        using var stream = typeof(NaturalExperienceTests).Assembly.GetManifestResourceStream("NexFlow.Tests.Concierge.NaturalExperienceEvaluation.json")!;
        using var bytes = new MemoryStream(); stream.CopyTo(bytes); return bytes.ToArray();
    }
    private static EvaluationCase[] Evaluation() => JsonSerializer.Deserialize<EvaluationCase[]>(EvaluationBytes(),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    public static IEnumerable<object[]> EvaluationIds() => Evaluation().Select(c => new object[] { c.Id });

    [Theory]
    [MemberData(nameof(EvaluationIds))]
    public async Task Frozen_mechanism_evaluation_preserves_facts_current_context_and_no_unapproved_effects(string id)
    {
        var row = Evaluation().Single(c => c.Id == id); var f = Create();
        if (row.Kind == "menu") await Say(f, "Quiero reservar en Concepción", "seed");
        if (row.Kind == "manage") {
            await Say(f, "Soy Elena Flores, quiero reservar limpieza dental mañana a las 10:00 en Concepción", "seed");
            await Say(f, "Confirmar", "commit"); Assert.Single(f.Bookings);
        }
        var draft = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        var ai = Act(row.Text, row.Kind is "start" or "capability" or "manage" ? ConversationIntent.Reservation
            : row.Kind == "explore" ? ConversationIntent.ServiceQuery : ConversationIntent.General,
            row.Kind == "start" ? DialogueAct.StartOperation : row.Kind is "capability" or "manage" ? DialogueAct.OperationInformation : DialogueAct.Information);
        ai.Directive = row.Kind switch { "menu" => ConversationDirective.MainMenu, "professional" => ConversationDirective.ProfessionalAvailability,
            "manage" => ConversationDirective.ManageExistingReservation, _ => ConversationDirective.None };
        if (row.Kind == "ambiguous") ai.RequiresClarification = true;
        if (row.Kind == "explore") { ai.Service = "Evaluación odontológica"; ai.SearchTerm = row.Fragment;
            ai.Evidence.Add(new("Service", row.Fragment!, ai.Service)); }
        await Say(f, row.Text, id, ai);
        var state = await f.Context(); var answer = f.Sent[id].Content;
        Assert.False(string.IsNullOrWhiteSpace(answer)); Assert.Empty(f.Orders);
        if (row.Kind != "manage") Assert.Empty(f.Bookings); else Assert.Single(f.Bookings);
        switch (row.Kind) {
            case "menu": Assert.All(state.RecoveryOptions, o => Assert.Equal("Action", o.Kind));
                Assert.NotEmpty(state.RecoveryOptions); Assert.Equal(draft, AssistantConversationTests.Fixture.BusinessDraft(state)); break;
            case "professional": Assert.Contains("agenda de profesionales", answer); Assert.Contains("asesor", answer); Assert.Null(state.CurrentGoal); break;
            case "manage": Assert.Contains("asesor", answer); Assert.Null(state.CurrentGoal); break;
            case "start": Assert.Equal("RESERVATION", state.CurrentGoal); Assert.Equal("COLLECT_LOCATION", state.CurrentStep); break;
            case "explore": Assert.Contains("Evaluación odontológica", answer); Assert.Contains("60.00", answer); Assert.Null(state.SelectedServiceId); Assert.Null(state.CurrentGoal); break;
            default: Assert.Equal(draft, AssistantConversationTests.Fixture.BusinessDraft(state)); break;
        }
        Assert.InRange(f.AiCalls, 0, 1);
    }

    [Fact]
    public void Evaluation_inputs_are_frozen_separately_from_the_development_examples()
        => Assert.Equal("141744B0EBDF3A4EC3A42CE4C8C1EBEB44D59BB4C62B89C30E6274ED4FBF1EF7",
            Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(System.Text.Encoding.UTF8.GetString(EvaluationBytes()).Replace("\r\n", "\n")))));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_discarded_patient_name_is_not_identity_for_the_next_reservation(bool cta)
    {
        var f = Create(cta);
        await Say(f, "Soy Elena Flores, quiero reservar limpieza dental mañana a las 10:00 en Concepción", "old");
        Assert.Equal("Elena Flores", (await f.Context()).RealCustomerName);
        await Say(f, "cancelar", "discard"); await Say(f, "Sí", "yes");
        Assert.Null((await f.Context()).CurrentGoal); Assert.Null((await f.Context()).RealCustomerName);
        await Say(f, "Quiero reservar limpieza dental mañana a las 10:00 en Concepción", "new");
        Assert.Null((await f.Context()).RealCustomerName); Assert.Equal("COLLECT_CUSTOMERNAME", (await f.Context()).CurrentStep);
        await Say(f, "Sí", "not-consent"); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task The_main_menu_does_not_become_the_current_service_picker_or_erase_the_draft()
    {
        var f = Create(); await Say(f, "Quiero reservar en Concepción", "start");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        await Say(f, "menú", "menu");
        Assert.NotEmpty((await f.Context()).RecoveryOptions);
        Assert.All((await f.Context()).RecoveryOptions, o => Assert.Equal("Action", o.Kind));
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("Quero hacer una reserva")]
    [InlineData("Me gustaría que me ayuden a agendar")]
    public async Task Natural_reservation_preparation_does_not_require_a_patient_name_or_commit(string text)
    {
        var f = Create(); await Say(f, text, "start", Act(text, ConversationIntent.Reservation, DialogueAct.StartOperation));
        Assert.Equal("RESERVATION", (await f.Context()).CurrentGoal);
        Assert.Equal("COLLECT_LOCATION", (await f.Context()).CurrentStep);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Service_orientation_can_offer_only_real_information_without_selecting_a_treatment()
    {
        var f = Create(); const string text = "Quiero hacerme una consulta odontologixa";
        var ai = Act(text, ConversationIntent.ServiceQuery, DialogueAct.Information);
        ai.Service = "Evaluación odontológica"; ai.SearchTerm = "consulta odontologixa";
        ai.Evidence.Add(new("Service", "consulta odontologixa", "Evaluación odontológica"));
        await Say(f, text, "orientation", ai);
        Assert.Contains("Evaluación odontológica", f.Sent["orientation"].Content);
        Assert.Contains("60.00", f.Sent["orientation"].Content); Assert.Contains("reserv", f.Sent["orientation"].Content);
        Assert.Null((await f.Context()).SelectedServiceId); Assert.Null((await f.Context()).CurrentGoal);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Pilot_commerce_is_enforced_by_workspace_without_changing_licenses_or_another_business()
    {
        var pilot = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true, informationalCommerce: true);
        var normal = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true);
        await Say(pilot, "menú", "menu");
        Assert.DoesNotContain((await pilot.Context()).RecoveryOptions, o => o.Key is "Order" or "Request");
        Assert.Contains("ORDERS", pilot.Modules); Assert.Contains("REQUESTS", pilot.Modules);
        await Say(pilot, "Quiero dos panes integrales", "buy"); Assert.Empty((await pilot.Context()).OrderDraftItems);
        Assert.Contains("catálogo", pilot.Sent["buy"].Content); Assert.Empty(pilot.Orders);
        await Say(normal, "Quiero dos panes integrales", "buy");
        Assert.Equal(2, Assert.Single((await normal.Context()).OrderDraftItems).Quantity);
        await Say(pilot, "Que productos tienen", "catalog"); Assert.Contains("Pan integral", pilot.Sent["catalog"].Content);
        await Say(pilot, "Quiero reservar", "booking"); Assert.Equal("RESERVATION", (await pilot.Context()).CurrentGoal);
        Assert.Empty(pilot.Bookings); Assert.Empty(pilot.Orders);
    }

    [Theory]
    [InlineData("not-found")]
    [InlineData("foreign-id")]
    [InlineData("forged-evidence")]
    [InlineData("revoked")]
    [InlineData("ambiguous-service")]
    public async Task Semantic_service_suggestions_cannot_invent_entities_or_select_a_reservation(string kind)
    {
        var f = Create(); if (kind == "revoked") f.Modules.Remove("SERVICES");
        var text = kind == "ambiguous-service" ? "¿Me orientas acerca de evaluación dental?" : "Quiero una revisión de mis dientes";
        var ai = Act(text, ConversationIntent.ServiceQuery, DialogueAct.Information);
        ai.SearchTerm = "revisión de mis dientes";
        ai.Service = kind == "not-found" ? "Tratamiento inventado" : kind == "foreign-id" ? "service-b" : "Evaluación odontológica";
        if (kind == "ambiguous-service") { f.Services[0].Name = "Evaluación dental infantil"; f.Services[1].Name = "Evaluación dental adultos";
            ai.Service = f.Services[1].Name; ai.SearchTerm = "evaluación dental"; }
        ai.Evidence.Add(new("Service", kind == "forged-evidence" ? "frase no escrita" : ai.SearchTerm, ai.Service));
        await Say(f, text, "query", ai);
        Assert.DoesNotContain("60.00", f.Sent["query"].Content); Assert.Null((await f.Context()).SelectedServiceId);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("yes")]
    [InlineData("no")]
    [InlineData("expired")]
    [InlineData("foreign")]
    [InlineData("general")]
    public async Task A_mixed_discard_and_new_reservation_waits_for_a_bound_decision_and_preserves_only_verified_date(string decision)
    {
        var f = Create(); await Say(f, "Quiero dos panes integrales", "order");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        const string text = "Cancelo mi pedido, quiero reservar mañana";
        var ai = Act(text, decision == "general" ? ConversationIntent.General : ConversationIntent.Order, DialogueAct.Cancellation); ai.Directive = ConversationDirective.CancelCurrentFlow;
        ai.NextIntent = ConversationIntent.Reservation; ai.Date = "2026-11-03"; ai.Evidence.Add(new("Date", "mañana", ai.Date));
        await Say(f, text, "change", ai);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        var pending = await f.Context(); Assert.NotNull(pending.PendingReservationAfterDiscard);
        if (decision == "expired") f.Now = f.Now.AddMinutes(11);
        if (decision == "foreign") { pending.PendingReservationAfterDiscard = pending.PendingReservationAfterDiscard! with { WorkspaceId = Guid.NewGuid() };
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, pending, default); }
        await Say(f, decision == "no" ? "No" : "Sí", "answer"); var state = await f.Context();
        if (decision is "yes" or "general") { Assert.Equal("RESERVATION", state.CurrentGoal); Assert.Equal("2026-11-03", state.TargetDate);
            Assert.Empty(state.OrderDraftItems); Assert.Null(state.RealCustomerName); }
        else if (decision == "no") Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(state));
        else Assert.NotEqual("RESERVATION", state.CurrentGoal);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Read_only_pilot_prompt_omits_unused_order_edit_grammar_but_keeps_evidence_and_consent_rules()
    {
        var f = Create(); var prompts = new List<string>(); var router = new Mock<IAiRouter>();
        router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Callback((AiTaskType _, string prompt, string _, bool _, CancellationToken _) => prompts.Add(prompt)).ReturnsAsync("{\"Intent\":\"General\"}");
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => f.Now);
        var interpreter = new AiInterpreter(router.Object, clock.Object, f.ProfileReads.Object, NullLogger<AiInterpreter>.Instance);
        await interpreter.InterpretAsync(f.Workspace, "Necesito orientación", "", null, f.Modules, default);
        var pilot = new HashSet<string>(f.Modules); pilot.Remove("ORDERS"); pilot.Remove("REQUESTS");
        await interpreter.InterpretAsync(f.Workspace, "Necesito orientación", "", null, pilot, default);
        Assert.Contains("QuantityText", prompts[0]); Assert.DoesNotContain("QuantityText", prompts[1]);
        Assert.True(prompts[1].Length < prompts[0].Length - 1000);
        Assert.All(prompts, p => { Assert.Contains("Evidence:", p); Assert.Contains("Nunca devuelvas Confirmation", p); Assert.DoesNotContain(AssistantConversationTests.Fixture.Phone, p); });
        output.WriteLine($"Contract characters full={prompts[0].Length}; pilot={prompts[1].Length}; provider_tokens=UNKNOWN");
    }

    [Fact]
    public void Empty_response_plans_are_rejected_before_any_ai_or_outbound()
    {
        var f = Create();
        Assert.Throws<ArgumentException>(() => new ResponsePlan(f.Workspace, "empty", null, ConversationIntent.General, DialogueAct.Information,
            OperationStatus.Informational, [], [], [], null, null, null, " ", false));
        Assert.Empty(f.Sent); Assert.Equal(0, f.AiCalls); Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("license")]
    [InlineData("informational-service")]
    public async Task An_informational_suggestion_cannot_offer_reservations_without_the_required_capability(string restriction)
    {
        var f = Create(); if (restriction == "license") f.Modules.Remove("RESERVATIONS"); else f.Services[1].RequiresReservation = false;
        const string text = "Quisiera una valoración bucal";
        var ai = Act(text, ConversationIntent.ServiceQuery, DialogueAct.Information);
        ai.SearchTerm = "valoración bucal"; ai.Service = "Evaluación odontológica";
        ai.Evidence.Add(new("Service", ai.SearchTerm, ai.Service));
        await Say(f, text, "query", ai);
        Assert.Contains("60.00", f.Sent["query"].Content); Assert.DoesNotContain("reservar una cita", f.Sent["query"].Content);
        Assert.Empty(f.Bookings); Assert.Null((await f.Context()).SelectedServiceId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Location_abbreviations_keep_all_authorized_collisions_even_if_ai_proposes_one(bool ambiguous)
    {
        var f = Create();
        if (ambiguous) f.LocationReads.Setup(l => l.GetLocationsAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new LocationDto("north", "Concepción Norte", "Synthetic", null, null, true),
                new LocationDto("south", "Concepción Sur", "Synthetic", null, null, false)]);
        await Say(f, "Quiero reservar", "start");
        var ai = Act("Conce", ConversationIntent.Reservation, DialogueAct.RequestedData);
        ai.Location = ambiguous ? "Concepción Norte" : "Sede Concepcion"; ai.Evidence.Add(new("Location", "Conce", ai.Location));
        await Say(f, "Conce", "site", ai);
        if (ambiguous) { Assert.Null((await f.Context()).SelectedLocationId); Assert.Contains("Concepción Norte", f.Sent["site"].Content); Assert.Contains("Concepción Sur", f.Sent["site"].Content); }
        else Assert.Equal("sede-a", (await f.Context()).SelectedLocationId);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task A_morning_preference_never_prompts_with_an_afternoon_example_or_selects_a_time()
    {
        var f = Create(); await Say(f, "Quiero reservar limpieza dental en Concepción", "start");
        await Say(f, "Para mañana en la mañana", "period");
        Assert.NotNull((await f.Context()).PreferredTime); Assert.Null((await f.Context()).TargetTime);
        Assert.DoesNotContain("tarde", f.Sent["period"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("hora", f.Sent["period"].Content); Assert.Empty(f.Bookings);
    }

    [Fact]
    public async Task The_request_flow_blocks_only_the_opted_in_workspace_before_any_creation_or_handoff()
    {
        var pilot = Guid.NewGuid(); var other = Guid.NewGuid();
        var requests = new Mock<IRequestService>(MockBehavior.Strict); var handoff = new Mock<IHumanHandoffService>(MockBehavior.Strict);
        requests.Setup(r => r.CreateRequestAsync(other, "synthetic", "chat", It.IsAny<RequestType>(), It.IsAny<string>(), It.IsAny<string>(), "source", null, It.IsAny<CancellationToken>())).ReturnsAsync("request");
        handoff.Setup(h => h.EscalateToHumanAsync(other, "chat", HandoffReason.AiEscalation, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var flow = new RequestFlow(requests.Object, handoff.Object, Options.Create(new AssistantRecoveryOptions { InformationalCommerceWorkspaces = [pilot] }));
        Assert.Contains("catálogo", await flow.ProcessAsync(pilot, "synthetic", "Necesito cotización", "chat", "source", default));
        requests.VerifyNoOtherCalls(); handoff.VerifyNoOtherCalls();
        Assert.Contains("request", await flow.ProcessAsync(other, "synthetic", "Necesito cotización", "chat", "source", default));
    }

    [Fact]
    public async Task A_professional_question_without_reservations_cannot_advertise_an_unlicensed_agenda()
    {
        var f = Create(); f.Modules.Remove("RESERVATIONS"); const string text = "¿Qué profesional está libre mañana?";
        var ai = Act(text, ConversationIntent.General, DialogueAct.Information); ai.Directive = ConversationDirective.ProfessionalAvailability;
        await Say(f, text, "question", ai);
        Assert.DoesNotContain("Puedo revisar horarios de citas", f.Sent["question"].Content);
        Assert.Contains("asesor", f.Sent["question"].Content); Assert.Empty(f.EngineReads.Invocations); Assert.Empty(f.Bookings);
    }
}
