using Moq;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

// Real handler/pipeline, interpreter validation and durable SDK fake; controlled
// provider JSON, never evidence of Gemini/Groq's language understanding.
public sealed class AgentStabilizationTests(ITestOutputHelper output)
{
    private readonly WhatsAppConversationalRepairTests _driver = new(output);
    private Task Say(AssistantConversationTests.Fixture f, string text, string id, AiInterpretation? ai = null)
        => _driver.Speak(f, text, id, ai);

    private static AssistantConversationTests.Fixture Create(bool cta)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true, contextualFocus: cta)
            { Now = new(2026, 11, 1, 14, 0, 0, DateTimeKind.Utc) };
        f.Products[0].Name = "Cepillo dental"; f.Products[0].PriceMinorUnits = 1490;
        f.Products[1].Name = "Enjuague bucal sin alcohol 250 ml"; f.Products[1].PriceMinorUnits = 2400;
        f.Products[2].Name = "Hilo dental"; f.Products[2].PriceMinorUnits = 1290;
        f.Products.Add(new ProductDto { Id = "product-d", Name = "Pasta dental", PriceMinorUnits = 1850 });
        f.Services[1].Name = "Evaluación odontológica";
        f.Services.Add(new ServiceDto { Id = "service-c", Name = "Aplicación de flúor", RequiresReservation = true, DurationInMinutes = 30 });
        f.UseCatalogKnowledge();
        // The requested 15:00 Lima is real synthetic engine availability, not
        // selected from the fixture's unrelated suggested-time grid.
        f.EngineReads.Setup(e => e.GetAvailabilityAtAsync(f.Workspace, It.IsAny<string>(), It.IsAny<string>(),
                It.Is<DateTime>(d => d.Hour == 15), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, string _, string _, DateTime at, CancellationToken _) =>
                new[] { new TimeSlotDto(DateTime.SpecifyKind(at.AddHours(5), DateTimeKind.Utc),
                    DateTime.SpecifyKind(at.AddHours(5.5), DateTimeKind.Utc), true) });
        return f;
    }

    [Theory]
    [InlineData("enjuague bucal")]
    [InlineData("enjuague bocal")]
    [InlineData("enjuague bocal sin slcohol")]
    public void Catalog_spelling_evidence_can_identify_an_omitted_suffix_without_inventing_an_entity(string mention)
    {
        BusinessEntityOption[] options = [new("rinse", "Enjuague bucal sin alcohol 250 ml"), new("floss", "Hilo dental")];
        var result = new OfferingMentionResolver().ResolveCommercial(mention, options);
        Assert.True(result.TryGetValue(out var entity), result.Code); Assert.Equal("rinse", entity.Id);
    }

    [Theory]
    [InlineData("collision")]
    [InlineData("foreign")]
    [InlineData("fabricated")]
    [InlineData("qualifier")]
    public void Commercial_spelling_never_breaks_collisions_or_accepts_absent_entities_and_qualifiers(string kind)
    {
        BusinessEntityOption[] allowed = kind == "foreign" ? [new("own", "Hilo dental")]
            : [new("rinse", "Enjuague bucal sin alcohol 250 ml")];
        if (kind == "collision") allowed = [.. allowed, new("similar", "Enjuague bucal sin alcohol 500 ml")];
        var mention = kind == "fabricated" ? "rinse-invented" : kind == "qualifier" ? "enjuague bocal con alcohol" : "enjuague bocal sin slcohol";
        var result = new OfferingMentionResolver().ResolveCommercial(mention, allowed);
        Assert.False(result.TryGetValue(out _));
        if (kind == "collision") { Assert.Equal(ResolutionStatus.Ambiguous, result.Status); Assert.Equal(2, result.Candidates.Count); }
        else Assert.Empty(result.Candidates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Catalog_corrections_use_verified_products_and_never_claim_physical_stock(bool cta)
    {
        var f = Create(cta);
        await Say(f, "Que productos tienen", "catalog");
        const string first = "Esta disponible el enjuague bocal?";
        await Say(f, first, "rinse", new() { Intent = ConversationIntent.ProductQuery, Act = DialogueAct.Information,
            QueryKind = OfferingQueryKind.Specific, SearchTerm = "enjuague bocal", Question = OfferingQuestion.Stock,
            Evidence = [new("Act", first, nameof(DialogueAct.Information))] });
        Assert.Contains("Enjuague bucal sin alcohol", f.Sent["rinse"].Content);
        Assert.Contains("stock", f.Sent["rinse"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Cepillo", f.Sent["rinse"].Content);
        const string correction = "Dije si tiene disponible el enjuague bocal sin slcohol";
        await Say(f, correction, "corrected-rinse", new() { Intent = ConversationIntent.ProductQuery, Act = DialogueAct.Information,
            QueryKind = OfferingQueryKind.Specific, SearchTerm = "enjuague bocal sin slcohol", Question = OfferingQuestion.Stock,
            Evidence = [new("Act", correction, nameof(DialogueAct.Information))] });
        Assert.Contains("Enjuague bucal sin alcohol", f.Sent["corrected-rinse"].Content);
        Assert.Contains("stock", f.Sent["corrected-rinse"].Content, StringComparison.OrdinalIgnoreCase);
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        Assert.InRange(f.AiCalls, 0, 2);
    }

    [Theory]
    [InlineData("collision")]
    [InlineData("foreign")]
    [InlineData("fabricated-span")]
    [InlineData("revoked")]
    [InlineData("site")]
    public async Task Specific_semantic_queries_are_grounded_in_the_current_authorized_catalog_scope(string kind)
    {
        var f = Create(false); var foreign = Guid.NewGuid();
        var entity = "enjuague bocal";
        f.OfferingReads.Setup(o => o.GetProductsAsync(foreign, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ProductDto { Id = "foreign", Name = "Enjuague bucal sin alcohol", PriceMinorUnits = 9999 }]);
        if (kind == "collision") f.Products.Add(new ProductDto { Id = "similar", Name = "Enjuague bucal sin alcohol 500 ml", PriceMinorUnits = 4000 });
        if (kind == "foreign") f.Products.RemoveAt(1);
        if (kind == "revoked") f.Modules.Remove("CATALOG");
        if (kind == "site") { f.Products[1].LocationScope = "SPECIFIC"; f.Products[1].LocationIds = ["sede-b"]; }
        var text = "Esta disponible el " + entity + (kind == "site" ? " en Concepción?" : "?");
        await Say(f, text, "query", new() { Intent = ConversationIntent.ProductQuery, Act = DialogueAct.Information,
            SearchTerm = kind == "fabricated-span" ? "Hilo dental" : entity,
            Location = kind == "site" ? "Sede Concepcion" : null,
            QueryKind = OfferingQueryKind.Specific, Question = OfferingQuestion.Stock,
            Evidence = kind == "site" ? [new("Act", text, nameof(DialogueAct.Information)), new("Location", "Concepción", "Sede Concepcion")]
                : [new("Act", text, nameof(DialogueAct.Information))] });
        var query = f.Understood.LastOrDefault()?.Interpretation.OfferingQuery;
        if (query != null) Assert.Empty(query.Offerings);
        Assert.DoesNotContain("24.00", f.Sent["query"].Content); Assert.DoesNotContain("99.99", f.Sent["query"].Content);
        Assert.DoesNotContain("12.90", f.Sent["query"].Content); Assert.Null((await f.Context()).CurrentGoal);
        if (kind == "collision") Assert.Contains("varias", f.Sent["query"].Content, StringComparison.OrdinalIgnoreCase);
        if (kind == "revoked") f.OfferingReads.Verify(o => o.GetProductsAsync(f.Workspace, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        f.OfferingReads.Verify(o => o.GetProductsAsync(foreign, It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("current")]
    [InlineData("current-with-label")]
    [InlineData("expired")]
    [InlineData("foreign")]
    [InlineData("new-question")]
    public async Task Product_references_require_the_current_question_workspace_and_ttl(string kind)
    {
        var f = Create(true); await Say(f, "Cuanto cuesta el hilo dental?", "price");
        var state = await f.Context();
        if (kind == "expired") f.Now = f.Now.AddMinutes(11);
        if (kind == "foreign") new OfferingReference(Guid.NewGuid(), "PRODUCT", ["product-b"], null).Remember(state);
        if (kind == "new-question") ConversationQuestion.Remember(f.Workspace, "other", state,
            ExpectedReply.Information, ConversationIntent.Location, "¿Qué sede necesitas?", f.Now);
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        const string text = "Dije si está disponible";
        await Say(f, text, "reference", new() { Intent = ConversationIntent.ProductQuery, Act = DialogueAct.Information,
            UseOfferingReference = true, Question = OfferingQuestion.Stock,
            QueryKind = OfferingQueryKind.Specific, SearchTerm = kind == "current-with-label" ? "Hilo dental" : null,
            Evidence = [new("Act", text, nameof(DialogueAct.Information))] });
        if (kind.StartsWith("current", StringComparison.Ordinal)) { Assert.Contains("Hilo dental", f.Sent["reference"].Content); Assert.Contains("stock", f.Sent["reference"].Content); }
        else { Assert.DoesNotContain("12.90", f.Sent["reference"].Content); Assert.DoesNotContain("24.00", f.Sent["reference"].Content); }
        Assert.Empty(f.Orders); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_ordinal_from_a_replaced_question_cannot_select_a_previous_service(bool cta)
    {
        var f = Create(cta); await Say(f, "Quiero reservar", "start"); await Say(f, "Concepción", "site");
        Assert.Equal("Service", (await f.Context()).RecoveryOptions[1].Kind);
        // Persist a different active question while retaining the old list, as
        // can happen after an interruption. A resumed service picker would still
        // be a valid selection question and is intentionally not this scenario.
        var state = await f.Context();
        ConversationQuestion.Remember(f.Workspace, "new-question", state, ExpectedReply.Information,
            ConversationIntent.Location, "¿Qué información de la sede necesitas?", f.Now, "chat");
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        await Say(f, "La segunda", "ordinal");
        Assert.Null((await f.Context()).SelectedServiceId); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData(false, "Quieor hacer una reserva")]
    [InlineData(true, "Quieor hacer una reserva")]
    [InlineData(false, "Me gustaría preparar una cita")]
    [InlineData(true, "Me gustaría preparar una cita")]
    public async Task A_backed_new_reservation_intent_leaves_the_catalog_without_old_options_or_consent(bool cta, string text)
    {
        var f = Create(cta); await Say(f, "Que productos tienen", "catalog");
        await Say(f, text, "booking", new() { Intent = ConversationIntent.Reservation, Act = DialogueAct.StartOperation,
            Evidence = [new("Act", text, nameof(DialogueAct.StartOperation))] });
        var state = await f.Context();
        Assert.Equal("RESERVATION", state.CurrentGoal); Assert.Equal("COLLECT_LOCATION", state.CurrentStep);
        Assert.Equal(ExpectedReply.Selection, state.CurrentQuestion!.Expected);
        Assert.All(state.RecoveryOptions, o => Assert.Equal("Location", o.Kind));
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders); Assert.InRange(f.AiCalls, 0, 1);
        await Say(f, "Sí", "premature"); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_to_N_transcript_uses_current_ordinals_Lima_dates_and_only_current_explicit_consent(bool cta)
    {
        var f = Create(cta);
        await Say(f, "Hola", "A"); await Say(f, "Menu", "B"); await Say(f, "Ver mas", "C");
        await Say(f, "Que productos tienen", "D");
        const string e = "Esta disponible el enjuague bocal?";
        await Say(f, e, "E", new() { Intent = ConversationIntent.ProductQuery, Act = DialogueAct.Information,
            SearchTerm = "enjuague bocal", QueryKind = OfferingQueryKind.Specific, Question = OfferingQuestion.Stock,
            Evidence = [new("Act", e, nameof(DialogueAct.Information))] });
        const string correction = "Dije si tiene disponible el enjuague bocal sin slcohol";
        await Say(f, correction, "F", new() { Intent = ConversationIntent.ProductQuery, Act = DialogueAct.Information,
            SearchTerm = "enjuague bocal sin slcohol", QueryKind = OfferingQueryKind.Specific, Question = OfferingQuestion.Stock,
            Evidence = [new("Act", correction, nameof(DialogueAct.Information))] });
        Assert.Contains("24.00", f.Sent["F"].Content); Assert.Contains("stock", f.Sent["F"].Content);
        const string g = "Quieor hacer una reserva";
        await Say(f, g, "G", new() { Intent = ConversationIntent.Reservation, Act = DialogueAct.StartOperation,
            Evidence = [new("Act", g, nameof(DialogueAct.StartOperation))] });
        Assert.Equal("COLLECT_LOCATION", (await f.Context()).CurrentStep);
        await Say(f, "Quiero reservar", "H"); Assert.Empty(f.Bookings);
        await Say(f, "En concepcion", "I"); var site = await f.Context();
        Assert.Equal("sede-a", site.SelectedLocationId);
        Assert.Equal("service-b", site.RecoveryOptions[1].Key);
        await Say(f, "La segunda", "J"); Assert.Equal("service-b", (await f.Context()).SelectedServiceId);
        await Say(f, "El 2 de noviembre", "K"); Assert.Equal("2026-11-02", (await f.Context()).TargetDate);
        await Say(f, "Las 3 pm", "L"); Assert.Equal("15:00", (await f.Context()).TargetTime);
        await Say(f, "Elena", "M1"); Assert.Empty(f.Bookings);
        await Say(f, "Elena Flores", "M2"); var review = await f.Context();
        Assert.Equal("CONFIRM", review.CurrentStep); Assert.Equal(ExpectedReply.ConfirmReservation, review.CurrentQuestion!.Expected);
        Assert.Equal("Elena Flores", review.RealCustomerName); Assert.Empty(f.Bookings);
        await Say(f, "De acuerdo.", "ack"); Assert.Empty(f.Bookings);
        await Say(f, "Sí", "N"); await f.HandleTurn("Sí", "N");
        var booking = Assert.Single(f.Bookings).Value;
        Assert.Equal("service-b", booking.ServiceId); Assert.Empty(f.Orders);
        Assert.InRange(f.AiCalls, 0, 6);
    }
}
