using Moq;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.Dialogue.Understanding;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Persistence.Firestore;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class CandidateCertificationTests
{
    private static AssistantConversationTests.Fixture Create(bool commerce = true)
    {
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: commerce) {
            Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc)
        };
        f.UseCatalogKnowledge(); return f;
    }

    [Theory]
    [InlineData("PRODUCT", "1", false)]
    [InlineData("SERVICE", "Servicios", false)]
    [InlineData("PRODUCT", "Productos", true)]
    [InlineData("SERVICE", "2", true)]
    public async Task Chat_fallback_without_offering_query_persists_choice_and_delivers_selected_scope(string scope, string selection, bool booking)
    {
        // Exercise the existing interpreter -> ChatFlow route, bypassing the optional local commerce resolver.
        var f = Create(commerce: false);
        if (booking) {
            var draft = await f.Context(); draft.CurrentGoal = "BOOKING"; draft.CurrentStep = "COLLECT_SERVICE"; draft.SelectedLocationId = "sede-a";
            await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, draft, default);
        }
        var before = AssistantConversationTests.Fixture.BusinessDraft(await f.Context());
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProductQuery, QueryKind = OfferingQueryKind.Broad });
        var artifact = CatalogArtifact.Initialize(f.Workspace, scope);
        artifact.CompleteUpload("https://example.test/current.pdf", "synthetic-hash");
        f.CatalogReads.Setup(c => c.GetArtifactAsync(f.Workspace, scope, It.IsAny<CancellationToken>())).ReturnsAsync(artifact);
        await f.HandleTurn("¿Me puedes pasar el PDF?", "pdf");
        Assert.Null(Assert.Single(f.Understood).Interpretation.OfferingQuery);
        var recovered = new ContextRecoveryService(Mock.Of<IConversationCache>(), new FirestoreConversationStateRepository(f.Firestore.Db));
        var state = await recovered.GetOrRecoverContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, default);
        Assert.Equal(new[] { "ProductQuery", "ServiceQuery" }, state.RecoveryOptions.Select(o => o.Key));
        Assert.Empty(f.CatalogReads.Invocations);
        var calls = f.AiCalls;
        await f.HandleTurn(selection, "selected"); await f.HandleTurn(selection, "selected");
        var send = Assert.Single(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync");
        Assert.Equal(f.Workspace, send.Arguments[0]); Assert.Equal(scope, send.Arguments[6]);
        Assert.Equal(calls, f.AiCalls); Assert.Equal(before, AssistantConversationTests.Fixture.BusinessDraft(await f.Context()));
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Fact]
    public async Task Mixed_informational_turn_reaches_chat_fallback_with_commerce_enabled()
    {
        var f = Create();
        f.Interpretations.Enqueue(new() { Intent = ConversationIntent.ProductQuery, QueryKind = OfferingQueryKind.Broad });
        var artifact = CatalogArtifact.Initialize(f.Workspace, "SERVICE");
        artifact.CompleteUpload("https://example.test/services.pdf", "synthetic-hash");
        f.CatalogReads.Setup(c => c.GetArtifactAsync(f.Workspace, "SERVICE", It.IsAny<CancellationToken>())).ReturnsAsync(artifact);
        // This mixed query bypasses the local commerce shortcut and exercises its semantic fallback.
        await f.HandleTurn("¿Me puedes pasar el PDF y sus horarios?", "pdf");
        Assert.Null(Assert.Single(f.Understood).Interpretation.OfferingQuery);
        Assert.Equal(new[] { "ProductQuery", "ServiceQuery" }, (await f.Context()).RecoveryOptions.Select(o => o.Key));
        Assert.Empty(f.CatalogReads.Invocations);
        await f.HandleTurn("2", "selection");
        Assert.Equal("SERVICE", Assert.Single(f.Outbound.Invocations, i => i.Method.Name == "SendDocumentAsync").Arguments[6]);
        Assert.Equal(1, f.AiCalls); Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
    }

    [Theory]
    [InlineData("2026-10-22", "2026-10-23")]
    [InlineData("2026-11-05", "2026-11-06")]
    public async Task Recovered_booking_correction_uses_durable_date_not_an_old_relative_question(string target, string expected)
    {
        var f = Create(); var draft = await f.Context();
        draft.CurrentGoal = "BOOKING"; draft.CurrentStep = "COLLECT_TIME";
        draft.SelectedLocationId = "sede-a"; draft.SelectedServiceId = "service-a"; draft.TargetDate = target;
        draft.LastQuestion = "¿Qué día de la próxima semana deseas reservar?";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, draft, default);
        await f.HandleTurn("¿Cuánto dura ese servicio?", "interruption");
        Assert.Equal(target, (await f.Context()).TargetDate);
        await f.HandleTurn("mejor el viernes", "correction");
        Assert.Equal(expected, (await f.Context()).TargetDate); Assert.Null((await f.Context()).TargetTime);
        Assert.Empty(f.Bookings); Assert.Empty(f.Orders); Assert.Equal(0, f.AiCalls);
    }

    [Theory]
    [InlineData("este jueves", "2026-10-22")]
    [InlineData("el jueves", "2026-10-22")]
    public void Old_question_cannot_override_an_explicit_qualifier_or_a_durable_anchor(string text, string expected)
    {
        var date = new BusinessDateResolver().ResolveReservation(text, new(2026, 10, 20, 16, 0, 0, TimeSpan.FromHours(-5)),
            contextDate: new(2026, 10, 22), previousQuestion: "¿Qué día de la próxima semana deseas?");
        Assert.True(date.TryGetValue(out var value), date.Code); Assert.Equal(DateOnly.Parse(expected), value);
    }

    [Fact]
    public async Task A_question_from_a_different_step_does_not_supply_week_context()
    {
        var f = Create(); var draft = await f.Context();
        draft.CurrentGoal = "BOOKING"; draft.CurrentStep = "COLLECT_LOCATION"; draft.SelectedServiceId = "service-a";
        draft.LastQuestion = "¿Qué día de la próxima semana deseas?";
        await f.Contexts.SaveContextAsync(f.Workspace, AssistantConversationTests.Fixture.Phone, draft, default);
        await f.HandleTurn("en Concepción el jueves", "location");
        Assert.Equal("2026-10-22", (await f.Context()).TargetDate); Assert.Empty(f.Bookings);
    }

    [Theory]
    [InlineData("el jueves de la próxima semana", "2026-10-29")]
    [InlineData("el viernes de esta semana", "2026-10-23")]
    [InlineData("este jueves", "2026-10-22")]
    [InlineData("el 22 de octubre, jueves", "2026-10-22")]
    public void Current_explicit_week_or_date_wins_over_old_question_and_anchor(string text, string expected)
    {
        var date = new BusinessDateResolver().ResolveReservation(text, new(2026, 10, 20, 16, 0, 0, TimeSpan.FromHours(-5)),
            contextDate: new(2026, 11, 5), previousQuestion: "¿Qué día de la próxima semana deseas?");
        Assert.True(date.TryGetValue(out var value), date.Code); Assert.Equal(DateOnly.Parse(expected), value);
    }
}
