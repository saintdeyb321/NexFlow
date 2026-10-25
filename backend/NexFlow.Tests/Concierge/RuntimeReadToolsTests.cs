using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class RuntimeReadToolsTests
{
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
