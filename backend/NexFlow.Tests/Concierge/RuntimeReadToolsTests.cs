using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Offerings;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class RuntimeReadToolsTests
{
    [Fact]
    public async Task Shared_store_and_shared_tools_keep_businesses_contacts_modules_and_ids_separate()
    {
        var a = new AssistantConversationTests.Fixture(); var b = new AssistantConversationTests.Fixture();
        Assert.NotEqual(a.Workspace, b.Workspace);
        a.Modules.IntersectWith(["SERVICES", "RESERVATIONS", "FAQ"]);
        b.Modules.IntersectWith(["CATALOG", "ORDERS", "FAQ"]);
        a.EntitlementReads.Setup(r => r.GetAvailableModuleCodesAsync(b.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(() => b.Modules.ToArray());
        var store = new CatalogFixture(useRealSources: true);
        var profiles = new FirestoreBusinessProfileRepository(store.Firestore.Db);
        var catalog = new FirestoreCatalogRepository(store.Firestore.Db, store.Firestore.Resources);
        var locations = new FirestoreLocationRepository(store.Firestore.Db, store.Firestore.Resources);
        var hours = new FirestoreBusinessHoursRepository(store.Firestore.Db);
        var faqs = new FirestoreFaqRepository(store.Firestore.Db);
        var offerings = new OfferingService(catalog);
        await profiles.SaveProfileAsync(a.Workspace, new("Clínica A", "", "", "", "Actividad A"), default);
        await profiles.SaveProfileAsync(b.Workspace, new("Comercio B", "", "", "", "Actividad B"), default);
        await catalog.SaveItemAsync(a.Workspace, new ServiceDto { Id = "same-id", Name = "Servicio A", RequiresReservation = true, DurationInMinutes = 30 }, true, default);
        await catalog.SaveItemAsync(b.Workspace, new ProductDto { Id = "same-id", Name = "Producto B", PriceMinorUnits = 1250 }, true, default);
        foreach (var f in new[] { a, b })
            await faqs.SaveFaqAsync(f.Workspace, new() { Id = "same-faq", Question = "Consulta", Answer = f == a ? "Dato A" : "Dato B", IsActive = true }, true, default);
        var knowledge = new KnowledgeService(profiles, locations, hours, faqs, offerings, NullLogger<KnowledgeService>.Instance);
        var tools = new BusinessReadTools(knowledge, a.EntitlementReads.Object);
        Assert.Equal("Clínica A", (await tools.ReadAsync(tools.Open(a.Workspace), new KnowledgeRead(KnowledgeTopic.Profile), default)).Knowledge!.Profile!.Name);
        Assert.Equal("Comercio B", (await tools.ReadAsync(tools.Open(b.Workspace), new KnowledgeRead(KnowledgeTopic.Profile), default)).Knowledge!.Profile!.Name);
        Assert.Equal("Servicio A", Assert.Single((await tools.ReadAsync(tools.Open(a.Workspace), new KnowledgeRead(KnowledgeTopic.Services), default)).Knowledge!.Offerings).Name);
        Assert.Equal("Producto B", Assert.Single((await tools.ReadAsync(tools.Open(b.Workspace), new KnowledgeRead(KnowledgeTopic.Products), default)).Knowledge!.Offerings).Name);
        Assert.Null(await offerings.GetServiceByIdAsync(b.Workspace, "same-id", default));
        Assert.Null(await offerings.GetProductByIdAsync(a.Workspace, "same-id", default));
        var before = store.Firestore.ReadRpcCalls;
        Assert.Equal(BusinessReadStatus.Denied, (await tools.ReadAsync(tools.Open(a.Workspace), new KnowledgeRead(KnowledgeTopic.Products), default)).Status);
        b.Modules.Remove("FAQ");
        Assert.Equal(BusinessReadStatus.Denied, (await tools.ReadAsync(tools.Open(b.Workspace), new KnowledgeRead(KnowledgeTopic.Faqs), default)).Status);
        Assert.Equal(before, store.Firestore.ReadRpcCalls);
        var states = new FirestoreConversationStateRepository(store.Firestore.Db);
        await states.UpsertStateAsync(a.Workspace, "synthetic-contact-a", new() { CurrentGoal = "RESERVATION", SelectedServiceId = "same-id" }, default);
        await states.UpsertStateAsync(b.Workspace, "synthetic-contact-b", new() { CurrentGoal = "ORDER", Mode = "Human" }, default);
        Assert.Null(await states.GetStateAsync(a.Workspace, "synthetic-contact-b", default));
        Assert.Null(await states.GetStateAsync(b.Workspace, "synthetic-contact-a", default));
        Assert.Equal("RESERVATION", (await states.GetStateAsync(a.Workspace, "synthetic-contact-a", default))!.CurrentGoal);
        Assert.Equal("Human", (await states.GetStateAsync(b.Workspace, "synthetic-contact-b", default))!.Mode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Mvp_general_information_does_not_let_a_faq_replace_the_public_profile(bool matching)
    {
        const string text = "¿De qué trata este negocio?";
        foreach (var label in new[] { "Clínica sintética", "Comercio sintético" })
        {
            var f = new AssistantConversationTests.Fixture(); var faq = UseRealKnowledge(f);
            faq.Setup(r => r.GetFaqsAsync(f.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync([
                new FaqDto { Id = "faq", Question = matching ? text : "Otra consulta", Answer = "FAQ sintética", IsActive = true }]);
            f.ProfileReads.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessProfileDto(label, "PRIVATE-TAX", "", "", "Actividad pública vigente", "America/Lima"));
            f.Interpretations.Enqueue(new() { Intent = ConversationIntent.General, Act = DialogueAct.Information,
                Evidence = [new("Act", text, nameof(DialogueAct.Information))] });
            await f.HandleTurn(text, "general-profile");
            Assert.Contains(label, f.Sent["general-profile"].Content);
            Assert.Contains("Actividad pública vigente", f.Sent["general-profile"].Content);
            Assert.DoesNotContain("PRIVATE-TAX", f.Sent["general-profile"].Content);
            Assert.DoesNotContain("FAQ sintética", f.Sent["general-profile"].Content);
            Assert.All(f.FactualQueries, q => Assert.Equal(f.Workspace, q.Workspace));
            Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
            // An explicit FAQ intent must still consult this workspace's FAQ.
            f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Faq, SearchTerm = matching ? text : "Otra consulta" });
            await f.HandleTurn("¿Cuál es la respuesta a mi consulta frecuente?", "explicit-faq");
            Assert.Contains("FAQ sintética", f.Sent["explicit-faq"].Content);
            Assert.All(faq.Invocations, call => Assert.Equal(f.Workspace, call.Arguments[0]));
        }
    }

    [Theory]
    [InlineData("¿De qué trata este negocio?", ConversationIntent.General)]
    [InlineData("¿Qué actividad realizan aquí?", ConversationIntent.General)]
    [InlineData("¿A qué se dedican?", ConversationIntent.Profile)]
    public async Task Mvp_backed_business_information_reaches_public_workspace_facts(string text, ConversationIntent intent)
    {
        foreach (var label in new[] { "Clínica sintética", "Consultorio sintético" })
        {
            var f = new AssistantConversationTests.Fixture(conciergeCatalog: true);
            var faq = UseRealKnowledge(f);
            f.ProfileReads.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessProfileDto(label, "PRIVATE-TAX", "public@example.test", "", "Atención odontológica preventiva", "America/Lima"));
            f.Interpretations.Enqueue(new() { Intent = intent, Act = DialogueAct.Information,
                Evidence = [new("Act", text, nameof(DialogueAct.Information))] });
            await f.HandleTurn(text, "public-profile");
            Assert.Contains(label, f.Sent["public-profile"].Content);
            Assert.Contains("Atención odontológica preventiva", f.Sent["public-profile"].Content);
            Assert.DoesNotContain("PRIVATE-TAX", f.Sent["public-profile"].Content);
            Assert.All(f.FactualQueries, q => Assert.Equal(f.Workspace, q.Workspace));
            Assert.Contains(f.FactualQueries, q => q.Topic == KnowledgeTopic.Profile);
            Assert.All(faq.Invocations, call => Assert.Equal(f.Workspace, call.Arguments[0]));
            Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        }
    }

    [Theory]
    [InlineData("ambiguous")]
    [InlineData("unbacked")]
    [InlineData("provider")]
    [InlineData("rate")]
    public async Task Mvp_uncertainty_is_not_business_absence_or_transaction_consent(string reason)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true); UseRealKnowledge(f);
        const string text = "¿De qué trata este negocio?";
        f.Interpretations.Enqueue(new() { Intent = reason == "provider" ? ConversationIntent.ProviderUnavailable
            : reason == "rate" ? ConversationIntent.RateLimited : ConversationIntent.General,
            Act = DialogueAct.Information, RequiresClarification = reason == "ambiguous",
            Evidence = reason == "unbacked" ? [] : [new("Act", text, nameof(DialogueAct.Information))] });
        await f.HandleTurn(text, "uncertain");
        Assert.DoesNotContain("No tengo información registrada", f.Sent["uncertain"].Content);
        Assert.DoesNotContain(f.FactualQueries, q => q.Topic == KnowledgeTopic.Faqs);
        Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData(false, "No tengo información registrada")]
    [InlineData(true, "No puedo consultar la información")]
    public async Task Mvp_profile_absence_and_repository_failure_have_distinct_factual_responses(bool unavailable, string expected)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true); UseRealKnowledge(f);
        if (unavailable) f.ProfileReads.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("synthetic unavailable"));
        else f.ProfileReads.Setup(p => p.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync((BusinessProfileDto?)null);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Profile });
        await f.HandleTurn("¿A qué se dedican?", "missing-profile");
        Assert.Contains(expected, f.Sent["missing-profile"].Content);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders); Assert.Null((await f.Context()).CurrentGoal);
    }

    internal static Mock<IFaqRepository> UseRealKnowledge(AssistantConversationTests.Fixture f)
    {
        var faq = new Mock<IFaqRepository>(MockBehavior.Strict);
        faq.Setup(r => r.GetFaqsAsync(f.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<FaqDto>());
        var real = new KnowledgeService(f.ProfileReads.Object, f.LocationReads.Object, f.HoursReads.Object,
            faq.Object, f.OfferingReads.Object, NullLogger<KnowledgeService>.Instance);
        f.KnowledgeReads.Setup(k => k.QueryAsync(f.Workspace, It.Is<BusinessKnowledgeSnapshot>(s => s.WorkspaceId == f.Workspace),
            It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>()))
            .Returns((Guid w, BusinessKnowledgeSnapshot s, KnowledgeQuery q, CancellationToken ct) => {
                f.FactualQueries.Add((w, q.Topic)); return real.QueryAsync(w, s, q, ct);
            });
        return faq;
    }

    [Fact]
    public async Task Source_denial_remains_denial_and_discards_every_payload_field()
    {
        var f = new AssistantConversationTests.Fixture();
        f.KnowledgeReads.Setup(k => k.QueryAsync(f.Workspace, It.IsAny<BusinessKnowledgeSnapshot>(),
            It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(new KnowledgeResult {
                Source = KnowledgeTopic.Faqs, Status = KnowledgeStatus.Denied, Facts = "must not escape"
            });
        var tools = new BusinessReadTools(f.KnowledgeReads.Object, f.EntitlementReads.Object);
        var result = await tools.ReadAsync(tools.Open(f.Workspace), new KnowledgeRead(KnowledgeTopic.Faqs), default);
        Assert.Equal(BusinessReadStatus.Denied, result.Status);
        Assert.Empty(result.Facts); Assert.Null(result.Knowledge); Assert.Null(result.Slots);
    }

    private static BusinessReadTools Tools(AssistantConversationTests.Fixture f, Mock<IFaqRepository>? faqs = null) => new(
        new KnowledgeService(f.ProfileReads.Object, f.LocationReads.Object, f.HoursReads.Object,
            (faqs ?? new Mock<IFaqRepository>(MockBehavior.Strict)).Object, f.OfferingReads.Object, NullLogger<KnowledgeService>.Instance),
        f.EntitlementReads.Object, f.EngineReads.Object);

    [Fact]
    public async Task Same_tools_keep_two_businesses_prices_faq_and_public_profile_isolated()
    {
        var clinic = new AssistantConversationTests.Fixture(conciergeCatalog: true);
        var shop = new AssistantConversationTests.Fixture(intelligentCommerce: true);
        foreach (var f in new[] { clinic, shop })
        {
            var faq = new Mock<IFaqRepository>(MockBehavior.Strict);
            var label = f == clinic ? "Clínica sintética" : "Tienda sintética";
            faq.Setup(r => r.GetFaqsAsync(f.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(new[] {
                new FaqDto { Question = "Acceso", Answer = label, IsActive = true } });
            f.ProfileReads.Setup(r => r.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new BusinessProfileDto(label, "PRIVATE-TAX-ID", "public@example.test", "", "Actividad declarada", "America/Lima"));
            var tools = Tools(f, faq); var turn = tools.Open(f.Workspace);
            var profile = await tools.ReadAsync(turn, new KnowledgeRead(KnowledgeTopic.Profile), default);
            Assert.Equal(f.Workspace, profile.WorkspaceId); Assert.Equal(label, profile.Knowledge!.Profile!.Name);
            Assert.DoesNotContain("PRIVATE-TAX-ID", JsonSerializer.Serialize(profile));
            var product = await tools.ReadAsync(turn, new KnowledgeRead(KnowledgeTopic.Products), default);
            Assert.Equal(f.Products[0].PriceMinorUnits.ToString(), product.Facts.First(p => p.Key == "PriceMinorUnits").Value);
            var answers = await tools.ReadAsync(turn, new KnowledgeRead(KnowledgeTopic.Faqs), default);
            Assert.Contains(label, answers.Knowledge!.Facts);
            Assert.All(f.OfferingReads.Invocations, call => Assert.Equal(f.Workspace, call.Arguments[0]));
            Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        }
    }

    [Theory]
    [InlineData(KnowledgeTopic.Products, "CATALOG")]
    [InlineData(KnowledgeTopic.Services, "SERVICES")]
    [InlineData(KnowledgeTopic.Locations, "LOCATIONS")]
    [InlineData(KnowledgeTopic.BusinessHours, "BUSINESS_HOURS")]
    [InlineData(KnowledgeTopic.Faqs, "FAQ")]
    public async Task Revocation_prevents_any_new_restricted_read(KnowledgeTopic topic, string module)
    {
        var f = new AssistantConversationTests.Fixture(); var faq = new Mock<IFaqRepository>(MockBehavior.Strict);
        faq.Setup(r => r.GetFaqsAsync(f.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<FaqDto>());
        var tools = Tools(f, faq); var turn = tools.Open(f.Workspace);
        Assert.NotEqual(BusinessReadStatus.Denied, (await tools.ReadAsync(turn, new KnowledgeRead(topic), default)).Status);
        f.Modules.Remove(module);
        f.OfferingReads.Invocations.Clear(); f.LocationReads.Invocations.Clear(); f.HoursReads.Invocations.Clear(); faq.Invocations.Clear();
        Assert.Equal(BusinessReadStatus.Denied, (await tools.ReadAsync(turn, new KnowledgeRead(topic), default)).Status);
        Assert.Empty(f.OfferingReads.Invocations); Assert.Empty(f.LocationReads.Invocations); Assert.Empty(f.HoursReads.Invocations); faq.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Pagination_limits_results_and_fourth_tool_has_no_repository_access()
    {
        var f = new AssistantConversationTests.Fixture(intelligentCommerce: true); var tools = Tools(f); var turn = tools.Open(f.Workspace);
        var first = await tools.ReadAsync(turn, new KnowledgeRead(KnowledgeTopic.Products, Limit: 1), default);
        Assert.Single(first.Knowledge!.Offerings); Assert.True(first.Knowledge.HasMore);
        var second = await tools.ReadAsync(turn, new KnowledgeRead(KnowledgeTopic.Products, Offset: 1, Limit: 1), default);
        Assert.NotEqual(first.Knowledge.Offerings[0].Id, second.Knowledge!.Offerings[0].Id);
        await tools.ReadAsync(turn, new KnowledgeRead(KnowledgeTopic.Profile), default);
        var reads = f.ProfileReads.Invocations.Count;
        Assert.Equal(BusinessReadStatus.LimitReached, (await tools.ReadAsync(turn, new KnowledgeRead(KnowledgeTopic.Profile), default)).Status);
        Assert.Equal(reads, f.ProfileReads.Invocations.Count);
        Assert.Equal(BusinessReadStatus.Invalid, (await tools.ReadAsync(tools.Open(f.Workspace), new KnowledgeRead(KnowledgeTopic.Products, Limit: 100), default)).Status);
    }

    [Fact]
    public async Task Source_failure_is_not_absence_and_revocation_during_read_suppresses_facts()
    {
        var f = new AssistantConversationTests.Fixture(); var tools = Tools(f);
        f.HoursReads.Setup(r => r.GetBusinessHoursAsync(f.Workspace, null, It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("unavailable"));
        Assert.Equal(BusinessReadStatus.Unavailable, (await tools.ReadAsync(tools.Open(f.Workspace), new KnowledgeRead(KnowledgeTopic.BusinessHours), default)).Status);
        f.ProfileReads.Setup(r => r.GetProfileAsync(f.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync((BusinessProfileDto?)null);
        Assert.Equal(BusinessReadStatus.NotFound, (await tools.ReadAsync(tools.Open(f.Workspace), new KnowledgeRead(KnowledgeTopic.Profile), default)).Status);
        var faq = new Mock<IFaqRepository>(); faq.Setup(r => r.GetFaqsAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { f.Modules.Remove("FAQ"); return new[] { new FaqDto { Answer = "Restricted fact" } }; });
        var revoked = await Tools(f, faq).ReadAsync(tools.Open(f.Workspace), new KnowledgeRead(KnowledgeTopic.Faqs), default);
        Assert.Equal(BusinessReadStatus.Denied, revoked.Status); Assert.Empty(revoked.Facts); Assert.Null(revoked.Knowledge);
    }

    [Fact]
    public async Task Availability_is_read_only_and_needs_reservations_services_not_public_hours_or_locations()
    {
        var f = new AssistantConversationTests.Fixture(); f.Modules.IntersectWith(["SERVICES", "RESERVATIONS"]);
        var tools = Tools(f); var query = new AvailabilityRead("sede-a", "service-a", new(2026, 10, 22), new(10, 0));
        var result = await tools.ReadAsync(tools.Open(f.Workspace), query, default);
        Assert.Equal(BusinessReadStatus.Found, result.Status); Assert.Single(result.Slots!);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        f.Modules.Remove("SERVICES"); f.EngineReads.Invocations.Clear();
        Assert.Equal(BusinessReadStatus.Denied, (await tools.ReadAsync(tools.Open(f.Workspace), query, default)).Status);
        Assert.Empty(f.EngineReads.Invocations);
    }

    [Fact]
    public async Task Location_references_do_not_create_a_new_informational_question_and_cancellation_propagates()
    {
        var f = new AssistantConversationTests.Fixture(); var tools = Tools(f);
        var result = await tools.ReadAsync(tools.Open(f.Workspace), new KnowledgeRead(KnowledgeTopic.Locations), default);
        Assert.NotEmpty(result.Knowledge!.References); Assert.Empty(result.Knowledge.LocationChoices);
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tools.ReadAsync(tools.Open(f.Workspace), new KnowledgeRead(KnowledgeTopic.Profile), stop.Token));
    }

    [Theory]
    [InlineData("¿Qué actividad realizan aquí?")]
    [InlineData("¿A qe se dedican en este negocio?")]
    public async Task Semantic_profile_queries_use_the_same_authorized_read_without_creating_drafts(string text)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Profile });
        await f.HandleTurn(text, "profile");
        Assert.Single(f.FactualQueries); Assert.Equal(KnowledgeTopic.Profile, f.FactualQueries[0].Topic);
        Assert.Equal(1, f.AiCalls); Assert.Null((await f.Context()).CurrentGoal); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }
}
