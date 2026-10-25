using Grpc.Core;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests;

public sealed class AssistantLicenseBudgetTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Handler_never_reads_faqs_without_a_current_license_including_after_revocation(bool initiallyLicensed)
    {
        var f = new AssistantConversationTests.Fixture();
        var faqs = new Mock<IFaqRepository>(MockBehavior.Strict);
        faqs.Setup(r => r.GetFaqsAsync(f.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new FaqDto { Question = "consulta sobre condiciones de atención", Answer = "Hecho FAQ autorizado", IsActive = true } });
        var knowledge = new KnowledgeService(f.ProfileReads.Object, f.LocationReads.Object, f.HoursReads.Object,
            faqs.Object, f.OfferingReads.Object, NullLogger<KnowledgeService>.Instance);
        f.KnowledgeReads.Setup(k => k.QueryAsync(f.Workspace, It.IsAny<BusinessKnowledgeSnapshot>(), It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>()))
            .Returns((Guid w, BusinessKnowledgeSnapshot snapshot, KnowledgeQuery query, CancellationToken ct) => knowledge.QueryAsync(w, snapshot, query, ct));
        if (initiallyLicensed)
        {
            f.Modules.Add("FAQ");
            f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Faq });
            await f.HandleTurn("consulta sobre condiciones de atención", "licensed");
            Assert.Contains("Hecho FAQ autorizado", f.Sent["licensed"].Content);
            faqs.Verify(r => r.GetFaqsAsync(f.Workspace, It.IsAny<CancellationToken>()), Times.Once);
            faqs.Invocations.Clear();
        }
        f.Modules.Remove("FAQ");
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Faq });
        await f.HandleTurn("consulta sobre condiciones de atención", "unlicensed");
        faqs.VerifyNoOtherCalls();
        Assert.DoesNotContain("Hecho FAQ autorizado", f.Sent["unlicensed"].Content);
        Assert.Contains("No hay información", f.Sent["unlicensed"].Content);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task General_chat_fallback_does_not_read_unlicensed_faqs_or_block_public_profile_information()
    {
        var f = new AssistantConversationTests.Fixture(); f.Modules.Remove("FAQ");
        var faqs = new Mock<IFaqRepository>(MockBehavior.Strict);
        var knowledge = new KnowledgeService(f.ProfileReads.Object, f.LocationReads.Object, f.HoursReads.Object,
            faqs.Object, f.OfferingReads.Object, NullLogger<KnowledgeService>.Instance);
        var chat = new ChatFlow(knowledge, f.EntitlementReads.Object, Mock.Of<ILocationResolverService>(),
            NullLogger<ChatFlow>.Instance, f.CatalogReads.Object, Mock.Of<INotificationService>());
        Assert.Contains("Negocio real", (await chat.ProcessAsync(f.Workspace, "consulta", new() { Intent = ConversationIntent.General }, null, default)).Text);
        Assert.Contains("No hay información", (await chat.ProcessAsync(f.Workspace, "consulta", new() { Intent = ConversationIntent.Faq }, null, default)).Text);
        faqs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Greeting_natural_product_query_then_numeric_selection_use_a_valid_budget_document()
    {
        var f = new AssistantConversationTests.Fixture();
        // Model the server constraint missing from the in-memory RPC transport.
        f.Firestore.BeforeCommit = commit => commit.Writes.Any(w => w.Update.Name.Split('/').Any(
            segment => segment.StartsWith("__", StringComparison.Ordinal) && segment.EndsWith("__", StringComparison.Ordinal)))
            ? new RpcException(new Status(StatusCode.InvalidArgument, "Reserved document identifier")) : null;
        await f.Turn("Hola", "greeting");
        Assert.Equal(0, f.AiCalls);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProductQuery, QueryKind = OfferingQueryKind.Broad });
        await f.Turn("que productos tiene?", "natural");
        await f.Turn("menú", "optional-menu");
        await f.Turn("1", "selection");
        Assert.Equal(1, f.AiCalls);
        Assert.Equal(4, f.Sent.Count);
        Assert.Equal(2, f.FactualQueries.Count);
        Assert.All(f.FactualQueries, q => Assert.Equal((f.Workspace, KnowledgeTopic.Products), q));
        Assert.NotNull(f.Firestore.Read($"workspaces/{f.Workspace}/conversationStates/ai-budget"));
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("SERVICES,RESERVATIONS", "ServiceQuery,Reservation,Support")]
    [InlineData("CATALOG,ORDERS", "ProductQuery,Order,Support")]
    [InlineData("CATALOG,SERVICES,RESERVATIONS,ORDERS,LOCATIONS,BUSINESS_HOURS,REQUESTS", "ProductQuery,ServiceQuery,Reservation,Order,Location,BusinessHours,Request,Support")]
    [InlineData("", "Support")]
    public async Task Menu_contains_only_current_licensed_actions_and_keeps_human_assistance(string modules, string expected)
    {
        var f = new AssistantConversationTests.Fixture();
        f.Modules.IntersectWith(modules.Split(',', StringSplitOptions.RemoveEmptyEntries));
        await f.Turn("menú");
        var actions = new List<string>();
        while (true)
        {
            var state = await f.Context();
            actions.AddRange(state.RecoveryOptions.Select(o => o.Key));
            if (!state.RecoveryHasMore) break;
            await f.Turn("ver más");
        }
        Assert.Equal(expected.Split(','), actions);
        Assert.Equal(0, f.AiCalls);
        await f.Turn("asesor");
        Assert.Equal(1, f.Handoffs);
    }

    [Theory]
    [InlineData(ConversationIntent.ProductQuery, "que productos tiene?", "productos", 1)]
    [InlineData(ConversationIntent.Location, "¿dónde están?", "ubicaciones", 0)]
    [InlineData(ConversationIntent.BusinessHours, "¿qué horarios tienen?", "horarios", 0)]
    [InlineData(ConversationIntent.Location, "indícame la dirección exacta", "ubicaciones", 1)]
    [InlineData(ConversationIntent.BusinessHours, "a qué hora abren", "horarios", 1)]
    public async Task Unlicensed_information_is_denied_for_natural_and_fast_paths_without_losing_the_draft(
        ConversationIntent intent, string text, string subject, int aiCalls)
    {
        var f = new AssistantConversationTests.Fixture();
        f.Modules.IntersectWith(["SERVICES", "RESERVATIONS"]);
        await f.SeedDraft("RESERVATION");
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = intent });
        await f.Turn(text);
        Assert.Contains(subject, Assert.Single(f.Sent.Values).Content);
        Assert.Contains("No hay información", Assert.Single(f.Sent.Values).Content);
        Assert.Empty(f.FactualQueries);
        Assert.Equal(aiCalls, f.AiCalls);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("LOCATIONS", "Location")]
    [InlineData("BUSINESS_HOURS", "BusinessHours")]
    public async Task Revoked_informational_module_cannot_execute_a_previous_menu_selection(string module, string action)
    {
        var f = new AssistantConversationTests.Fixture();
        f.Modules.IntersectWith([module]);
        await f.Turn("menú");
        Assert.Equal(action, (await f.Context()).RecoveryOptions[0].Key);
        f.Modules.Remove(module);
        await f.Turn("1");
        Assert.Contains("ya no está disponible", f.Sent["turn-2"].Content);
        Assert.Equal("Support", Assert.Single((await f.Context()).RecoveryOptions).Key);
        Assert.Empty(f.FactualQueries); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData(ConversationIntent.Location)]
    [InlineData(ConversationIntent.BusinessHours)]
    public async Task Direct_chat_flow_checks_current_workspace_modules_before_reading_business_data(ConversationIntent intent)
    {
        var allowed = Guid.NewGuid(); var denied = Guid.NewGuid();
        var entitlements = new Mock<IEntitlementService>(MockBehavior.Strict);
        entitlements.Setup(e => e.GetAvailableModuleCodesAsync(allowed, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "LOCATIONS", "BUSINESS_HOURS" });
        entitlements.Setup(e => e.GetAvailableModuleCodesAsync(denied, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "SERVICES", "RESERVATIONS" });
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        knowledge.Setup(k => k.QueryAsync(allowed, It.Is<BusinessKnowledgeSnapshot>(s => s.WorkspaceId == allowed), It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, BusinessKnowledgeSnapshot _, KnowledgeQuery q, CancellationToken _) =>
                new KnowledgeResult { Status = KnowledgeStatus.Found, Source = q.Topic, LocationId = q.LocationId, Facts = "Authorized workspace facts" });
        var chat = new ChatFlow(knowledge.Object, entitlements.Object, Mock.Of<ILocationResolverService>(),
            NullLogger<ChatFlow>.Instance, Mock.Of<ICatalogGenerationService>(), Mock.Of<INotificationService>());
        Assert.Contains("Authorized workspace facts", (await chat.ProcessAsync(allowed, "consulta", new() { Intent = intent }, null, default)).Text);
        Assert.Contains("No hay información", (await chat.ProcessAsync(denied, "consulta", new() { Intent = intent }, null, default)).Text);
        knowledge.Verify(k => k.QueryAsync(denied, It.IsAny<BusinessKnowledgeSnapshot>(), It.IsAny<KnowledgeQuery>(), default), Times.Never);
    }

    [Theory]
    [InlineData(ConversationIntent.Location)]
    [InlineData(ConversationIntent.BusinessHours)]
    public async Task Direct_booking_flow_does_not_bypass_informational_module_checks(ConversationIntent intent)
    {
        var f = new AssistantConversationTests.Fixture();
        f.Modules.IntersectWith(["SERVICES", "RESERVATIONS"]);
        await f.SeedDraft("RESERVATION");
        var context = await f.Context();
        var before = AssistantConversationTests.Fixture.BusinessDraft(context);
        var response = await f.Booking.ProcessAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, "chat", context,
            new() { Intent = intent }, "Ana", "direct", default);
        Assert.Contains("No hay información", response);
        Assert.Empty(f.FactualQueries);
        Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
    }

    [Fact]
    public async Task Reservations_keep_internal_locations_hours_and_availability_without_independent_information_modules()
    {
        var f = new AssistantConversationTests.Fixture { Now = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc) };
        f.Modules.IntersectWith(["SERVICES", "RESERVATIONS"]);
        await f.Turn("quiero reservar");
        await f.Turn("1"); Assert.Equal("sede-a", (await f.Context()).SelectedLocationId);
        await f.Turn("2"); Assert.Equal("service-b", (await f.Context()).SelectedServiceId);
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.Reservation, Date = "2026-10-20" }); await f.Turn("el 20 de octubre");
        await f.Turn("10:00"); Assert.Equal("COLLECT_CUSTOMERNAME", (await f.Context()).CurrentStep);
        await f.Turn("Ana Pérez"); Assert.Equal("CONFIRM", (await f.Context()).CurrentStep);
        await f.Turn("sí", "commit"); await f.Turn("sí", "commit");
        var booking = Assert.Single(f.Bookings.Values);
        Assert.Equal(f.Workspace, booking.WorkspaceId);
        Assert.Equal(new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc), booking.StartTime);
    }
}

