using System.Text.Json;
using Moq;
using NexFlow.Application.Features.Automation.AgentV2;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Knowledge;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

// All interpretations/reviews here are injected. Existing read adapters,
// journal, CAS and intercepted outbound are real code over synthetic storage.
public sealed class AgentV2NaturalConversationTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("valid")]
    [InlineData("ambiguous")]
    [InlineData("stale-question")]
    [InlineData("foreign-id")]
    [InlineData("type-collision")]
    public async Task Preparation_references_require_current_question_and_one_authorized_entity_per_field(string mode)
    {
        var c = new AgentV2RuntimeTests.Case(); var origin = await c.F.Business.Context();
        origin.AgentV2 = new(1, "Information", mode == "ambiguous" ? ["service-a", "service-b", "sede-a"] : ["service-a", "sede-a"], "reference-source");
        origin.AgentV2 = origin.AgentV2 with { EntityReferences = mode == "ambiguous" ? [new("Service", "service-a"), new("Service", "service-b"), new("Location", "sede-a")]
            : [new(mode == "type-collision" ? "Product" : "Service", "service-a"), new("Location", "sede-a")] };
        ConversationQuestion.Remember(c.F.Business.Workspace, "reference-source", origin, ExpectedReply.Information,
            NexFlow.Domain.Enums.ConversationIntent.General, "Información de entidades verificadas", c.F.Business.Now, "chat");
        var questionId = mode == "stale-question" ? "obsolete" : origin.CurrentQuestion!.QuestionId;
        const string text = "Ayúdame a preparar esa atención en esa sede";
        var stage = await c.F.BookingDrafts.StageAsync(c.F.Request(text, "reference-current"), origin,
            new NexFlow.Application.Features.Automation.Agent.AgentBookingPatch(
                Location: new("sede-a", "esa sede", QuestionId: questionId),
                Service: new(mode == "foreign-id" ? "other-tenant-service" : "service-a", "esa atención", QuestionId: questionId)), 0, default);
        if (mode == "valid") { Assert.Null(stage.Clarification); Assert.Equal("sede-a", stage.Context.SelectedLocationId); Assert.Equal("service-a", stage.Context.SelectedServiceId); }
        else { Assert.NotNull(stage.Clarification); Assert.Null(stage.Context.SelectedLocationId); Assert.Null(stage.Context.SelectedServiceId); }
        Assert.Null(origin.SelectedServiceId); Assert.Null(origin.SelectedLocationId);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders);
    }
    private void Trace(string user, AgentV2Evaluation response) => output.WriteLine(JsonSerializer.Serialize(new {
        Evidence = "INJECTED_INTERPRETATION_AND_REVIEW_NOT_REAL_NLU", Engine = "AgentV2", User = user,
        Candidate = response.ProposedAnswer, Published = response.Durable.VerifiedText, response.Durable.Accepted,
        response.Durable.ModelCalls, response.Durable.ToolCalls, response.Durable.ElapsedMilliseconds,
        Tools = response.Tools, response.Trace, HTTP = 0 }));
    [Fact]
    public async Task Current_published_question_survives_history_truncation_without_using_a_stale_menu()
    {
        var c = new AgentV2RuntimeTests.Case();
        const string question = "¿Prefieres conocer la sede o revisar el precio de esa opción?";
        c.NaturalRead("list_services", facts => {
            var id = Id(facts, "Offering", "service-a");
            return new(string.Join(' ', Enumerable.Repeat("Podemos revisarlo con calma.", 12)) + " La opción es " + Anchor(id) + ". " + question, [id]);
        }); c.InjectSupportedReview();
        var first = await c.ConversationalTurn("Ayúdame a elegir", "long-question"); Assert.True(first.Durable.Accepted);
        Assert.True(first.Durable.VerifiedText!.Length > 250);
        c.Script.Enqueue(input => {
            Assert.EndsWith(question, input.GetProperty("Draft").GetProperty("Question").GetProperty("Prompt").GetString());
            return Task.FromResult(JsonSerializer.Serialize(new AgentV2ModelTurn([], new("¿Qué aspecto de la opción quieres que revise?", []))));
        }); c.InjectSupportedReview();
        var next = await c.ConversationalTurn("Esa opción, por favor", "reference"); Assert.True(next.Durable.Accepted);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders);
    }
    [Fact]
    public async Task Exhausted_real_client_window_returns_a_durable_safe_reply_without_extra_model_or_draft_loss()
    {
        var c = new AgentV2RuntimeTests.Case();
        for (var i = 0; i < 2; i++) {
            c.NaturalRead("list_services", facts => Option(facts, "OfferingPrice", "service-a")); c.InjectSupportedReview();
            Assert.True((await c.Turn("Consulta sintética", "quota-" + i)).Durable.Accepted);
        }
        var before = await c.F.Business.Context();
        var rejected = await c.Turn("Otra consulta", "quota-full");
        Assert.False(rejected.Durable.Accepted); Assert.True(rejected.Durable.Publishable);
        Assert.Equal(0, rejected.Durable.ModelCalls); Assert.Equal(4, c.Inputs.Count); Assert.Equal(2, c.ReviewInputs.Count);
        Assert.Equal(AssistantConversationTests.Fixture.BusinessDraft(before), AssistantConversationTests.Fixture.BusinessDraft(await c.F.Business.Context()));
        var replay = await c.Turn("Otra consulta", "quota-full"); Assert.Equal(rejected.Durable.VerifiedText, replay.Durable.VerifiedText);
        Assert.Equal(4, c.Inputs.Count); Assert.Equal(2, c.ReviewInputs.Count);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders);
    }

    [Fact]
    public async Task Availability_wording_uses_engine_UTC_result_converted_by_the_authorized_business_zone()
    {
        var c = new AgentV2RuntimeTests.Case(); c.Call("check_appointment_availability", new { locationId = "sede-a", serviceId = "service-a", date = "2026-11-04", time = "10:00" });
        c.Script.Enqueue(input => {
            var id = Id(input.GetProperty("Results")[0].GetProperty("Facts").EnumerateArray().ToArray(), "AvailableAppointment");
            return Task.FromResult(JsonSerializer.Serialize(new AgentV2ModelTurn([], new("En esta consulta aparece " + Anchor(id) + " ¿Quieres seguir preparando los datos?", [id]))));
        }); c.InjectSupportedReview();
        var result = await c.Turn("¿Hay espacio mañana a las diez?", "availability");
        Assert.True(result.Durable.Accepted); Assert.Contains("2026-11-04 10:00 (America/Lima)", result.Durable.VerifiedText);
        Assert.Contains("todavía no reservada", result.Durable.VerifiedText);
        c.F.Business.EngineReads.Verify(e => e.GetAvailabilityAtAsync(c.F.Business.Workspace, "sede-a", "service-a", new DateTime(2026, 11, 4, 10, 0, 0), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders);
    }
    [Fact]
    public async Task Private_profile_and_patient_fields_or_wrong_history_roles_do_not_enter_model_or_review_context()
    {
        var c = new AgentV2RuntimeTests.Case();
        c.F.Business.ProfileReads.Setup(p => p.GetProfileAsync(c.F.Business.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NexFlow.Application.Features.Business.BusinessProfileDto("Negocio sintético", "PRIVATE_TAX", "PRIVATE_EMAIL", "PRIVATE_PHONE", "Descripción pública"));
        var state = await c.F.Business.Context(); state.RealCustomerName = "PRIVATE_PATIENT";
        state.AgentV2 = new(1, "Information", []); // A context created by the previously published version.
        ConversationQuestion.Remember(c.F.Business.Workspace, "old-legacy-question", state, ExpectedReply.Information,
            NexFlow.Domain.Enums.ConversationIntent.General, "PRIVATE_PATIENT: resumen heredado", c.F.Business.Now, "chat");
        await c.F.Business.Contexts.SaveContextAsync(c.F.Business.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        c.F.History.Add(new() { Content = "PRIVATE_INTERNAL", Sender = NexFlow.Domain.Enums.SenderType.BusinessUser, Timestamp = c.F.Business.Now });
        c.F.History.Add(new() { Content = "PRIVATE_FUTURE", Sender = NexFlow.Domain.Enums.SenderType.AI, Timestamp = c.F.Business.Now.AddDays(1) });
        c.NaturalRead("list_services", facts => Option(facts, "OfferingPrice", "service-a")); c.InjectSupportedReview();
        var result = await c.Turn("Consulta sintética", "privacy"); Assert.True(result.Durable.Accepted);
        foreach (var input in c.Inputs.Concat(c.ReviewInputs))
            foreach (var secret in new[] { "PRIVATE_TAX", "PRIVATE_EMAIL", "PRIVATE_PHONE", "PRIVATE_PATIENT", "PRIVATE_INTERNAL", "PRIVATE_FUTURE" }) Assert.DoesNotContain(secret, input);
        Assert.DoesNotContain("PRIVATE_PATIENT", result.Durable.VerifiedText);
    }
    private static string Id(JsonElement[] facts, string key, string? entity = null)
        => facts.First(f => f.GetProperty("Key").GetString() == key && (entity == null || f.GetProperty("EntityId").GetString() == entity)).GetProperty("Id").GetString()!;
    private static string Anchor(string id) => "{{" + id + "}}";
    private static AgentV2Answer Option(JsonElement[] facts, string key, string? entity = null)
    {
        var id = Id(facts, key, entity); return new("Lo vemos con calma. Podemos revisar " + Anchor(id) + ". ¿Qué te gustaría precisar?", [id]);
    }

    [Fact]
    public async Task Clinic_multiturn_preparation_typo_location_and_price_interruption_preserve_context_without_writes()
    {
        var c = new AgentV2RuntimeTests.Case(); c.F.Business.Services[0].Description = "Consulta inicial para valoración profesional de molestias; sin diagnóstico por chat.";
        c.NaturalRead("list_services", facts => Option(facts, "Offering", "service-a")); c.InjectSupportedReview();
        var first = await c.ConversationalTurn("Me molesta un diente y no sé qué atención elegir", "need");
        Trace("Me molesta un diente y no sé qué atención elegir", first);
        Assert.True(first.Durable.Accepted, JsonSerializer.Serialize(first)); Assert.Contains("Lo vemos con calma", first.Durable.VerifiedText);
        Assert.Contains("Evaluación odontológica", first.Durable.VerifiedText); Assert.DoesNotContain("Extracción dental", first.Durable.VerifiedText);
        Assert.Contains(first.Tools!, t => t.Name == "list_services" && t.Facts.Any(f => f.Key == "OfferingDescription"));

        c.NaturalRead("list_locations", facts => Option(facts, "Location", "sede-a")); c.InjectSupportedReview();
        var place = await c.ConversationalTurn("Estoy por conepcion, ¿tienen sede ahí?", "place");
        Trace("Estoy por conepcion, ¿tienen sede ahí?", place);
        Assert.True(place.Durable.Accepted, JsonSerializer.Serialize(place)); Assert.Contains("Concepción", place.Durable.VerifiedText);
        using (var input = JsonDocument.Parse(c.Inputs[2])) Assert.Contains(input.RootElement.GetProperty("Recent").EnumerateArray(), r => r.GetProperty("Text").GetString()!.Contains("Evaluación odontológica"));
        // Six real admissions were used by these two turns. Simulate a user's
        // pause; keep the unchanged six-calls/minute limit in the real repository.
        c.F.Business.Now = c.F.Business.Now.AddMinutes(1);

        const string select = "Quieor esa atención en esa sede";
        var referencedQuestion = (await c.F.Business.Context()).CurrentQuestion!.QuestionId;
        c.Call("prepare_appointment", new { Service = new { Value = "service-a", Evidence = "esa atención", QuestionId = referencedQuestion },
            Location = new { Value = "sede-a", Evidence = "esa sede", QuestionId = referencedQuestion } });
        c.Script.Enqueue(input => {
            var id = Id(input.GetProperty("Results")[0].GetProperty("Facts").EnumerateArray().ToArray(), "PreparationStatus");
            return Task.FromResult(JsonSerializer.Serialize(new AgentV2ModelTurn([], new("Seguimos con " + Anchor(id) + " ¿Para qué fecha te gustaría revisar opciones?", [id]), "BookingPreparation")));
        }); c.InjectSupportedReview();
        var prepared = await c.ConversationalTurn(select, "prepare-natural"); Assert.True(prepared.Durable.Accepted, JsonSerializer.Serialize(prepared));
        Trace(select, prepared);
        var before = await c.F.Business.Context(); Assert.Equal("sede-a", before.SelectedLocationId); Assert.Equal("service-a", before.SelectedServiceId);
        Assert.Equal(ExpectedReply.RequestedData, before.CurrentQuestion!.Expected); Assert.Equal("prepare-natural", before.CurrentQuestion.SourceMessageId);

        c.NaturalRead("list_services", facts => {
            var id = Id(facts, "OfferingPrice", "service-a"); return new("Claro, el precio publicado es " + Anchor(id) + ". ¿Retomamos la fecha de tu preparación?", [id]);
        }); c.InjectSupportedReview();
        var price = await c.ConversationalTurn("Antes, ¿cuánto cuesta esa atención?", "interrupt");
        Trace("Antes, ¿cuánto cuesta esa atención?", price);
        Assert.True(price.Durable.Accepted); Assert.Contains("Evaluación odontológica: PEN 60.00", price.Durable.VerifiedText);
        var after = await c.F.Business.Context(); Assert.Equal(AssistantConversationTests.Fixture.BusinessDraft(before), AssistantConversationTests.Fixture.BusinessDraft(after));
        Assert.Equal(price.Durable.VerifiedText, after.LastQuestion); Assert.Equal("interrupt", after.CurrentQuestion!.SourceMessageId);
        Assert.Equal(ExpectedReply.Information, after.CurrentQuestion.Expected); Assert.Equal(4, c.ReviewInputs.Count);
        Assert.Equal(0, c.F.Business.AiCalls); Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders); Assert.Empty(c.F.Business.Sent);
    }

    [Fact]
    public async Task Hotel_uses_same_runtime_with_room_facts_and_policies_without_inventing_inventory()
    {
        var c = new AgentV2RuntimeTests.Case("accommodation"); c.Setting.RoomTypeServiceIds = ["service-a"];
        c.F.Business.Services[0].Name = "Habitación familiar"; c.F.Business.Services[0].PriceMinorUnits = 18500;
        c.NaturalRead("search_room_types", facts => {
            var price = Id(facts, "OfferingPrice", "service-a");
            return new("Podemos revisar esta opción: " + Anchor(price) + ". ¿Quieres conocer las condiciones del alojamiento?", [price]);
        }); c.InjectSupportedReview();
        var first = await c.ConversationalTurn("Vamos con un pequeño, ¿qué opciones ofrecen para alojarnos?", "stay");
        Trace("Vamos con un pequeño, ¿qué opciones ofrecen para alojarnos?", first);
        Assert.True(first.Durable.Accepted); Assert.Contains("Habitación familiar: PEN 185.00", first.Durable.VerifiedText);
        Assert.DoesNotContain("Evaluación odontológica", first.Durable.VerifiedText);
        c.F.Business.KnowledgeReads.Setup(k => k.QueryAsync(c.F.Business.Workspace, It.IsAny<BusinessKnowledgeSnapshot>(),
            It.Is<KnowledgeQuery>(q => q.Topic == KnowledgeTopic.Faqs), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResult { Status = KnowledgeStatus.Found, Source = KnowledgeTopic.Faqs, Facts = "Ingreso desde las 15:00; consultar condiciones para menores con el negocio." });
        c.NaturalRead("get_stay_rules", facts => Option(facts, "Faqs")); c.InjectSupportedReview();
        var second = await c.ConversationalTurn("Sí, esas condiciones, por favor", "rules");
        Trace("Sí, esas condiciones, por favor", second);
        Assert.True(second.Durable.Accepted); Assert.Contains("15:00", second.Durable.VerifiedText);
        using var input = JsonDocument.Parse(c.Inputs[2]);
        Assert.DoesNotContain(input.RootElement.GetProperty("Tools").EnumerateArray(), t => t.GetProperty("Name").GetString() is "prepare_appointment" or "check_room_inventory" or "prepare_stay");
        Assert.Contains(input.RootElement.GetProperty("Recent").EnumerateArray(), t => t.GetProperty("Text").GetString()!.Contains("Habitación familiar"));
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders);
    }

    [Fact]
    public async Task Natural_text_is_really_journaled_and_sent_once_through_intercepted_outbound()
    {
        var c = new AgentV2RuntimeTests.Case(channel: true);
        c.NaturalRead("list_services", facts => Option(facts, "OfferingPrice", "service-a")); c.InjectSupportedReview();
        await c.F.Business.HandleTurn("Necesito orientación", "natural-wire");
        var text = Assert.Single(c.F.WireTexts); Assert.StartsWith("Lo vemos con calma.", text); Assert.DoesNotContain("{{", text);
        output.WriteLine(JsonSerializer.Serialize(new { Evidence = "INJECTED_MODELS_REAL_JOURNAL_INTERCEPTED_WIRE", Engine = "AgentV2", Published = text, c.F.WireCalls }));
        Assert.Contains("PEN 60.00", text); Assert.DoesNotContain("Extracción dental", text);
        var receipt = await c.F.Journal.ObserveAsync(c.F.Request("Necesito orientación", "natural-wire"), default);
        Assert.Equal(text, receipt.Result!.VerifiedText); Assert.Null(receipt.Result.Proposal);
        await c.F.Business.HandleTurn("Necesito orientación", "natural-wire");
        Assert.Single(c.F.WireTexts); Assert.Single(c.ReviewInputs); Assert.Equal(2, c.Inputs.Count);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders); Assert.Equal(0, c.F.Business.AiCalls);
    }

    [Theory]
    [InlineData("revocation")]
    [InlineData("human")]
    [InlineData("cas")]
    public async Task Changes_during_independent_review_suppress_natural_publication(string change)
    {
        var c = new AgentV2RuntimeTests.Case(); c.NaturalRead("list_services", facts => Option(facts, "OfferingPrice", "service-a")); c.InjectSupportedReview();
        c.BeforeReview = async () => {
            if (change == "revocation") c.F.Business.Modules.Remove("SERVICES");
            else { var state = await c.F.Business.Context(); if (change == "human") state.Mode = "Human"; else state.TargetDate = "2026-11-09";
                await c.F.Business.Contexts.SaveContextAsync(c.F.Business.Workspace, AssistantConversationTests.Fixture.Phone, state, default); }
        };
        var result = await c.Turn("Orientación", "during-review");
        Assert.False(result.Durable.Accepted); Assert.False(result.Durable.Publishable); Assert.Empty(c.F.Business.Sent);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders);
    }

    [Fact]
    public async Task Rejected_or_unavailable_review_provides_only_selected_verified_facts_and_no_retry()
    {
        var c = new AgentV2RuntimeTests.Case(); c.NaturalRead("list_services", facts => {
            var price = Id(facts, "OfferingPrice", "service-a"); return new("La cita ya está registrada a ese precio: " + Anchor(price), [price]);
        });
        c.ReviewScript.Enqueue(_ => Task.FromResult(JsonSerializer.Serialize(new AgentV2SemanticReview("Unsupported", []))));
        var response = await c.Turn("Información", "denied-natural");
        Assert.False(response.Durable.Accepted); Assert.True(response.Durable.Publishable);
        Assert.Contains("Evaluación odontológica: PEN 60.00", response.Durable.VerifiedText); Assert.DoesNotContain("ya está registrada", response.Durable.VerifiedText);
        Assert.DoesNotContain("Extracción dental", response.Durable.VerifiedText);
        await c.Turn("Información", "denied-natural"); Assert.Single(c.ReviewInputs); Assert.Equal(2, c.Inputs.Count);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders);
    }
}
