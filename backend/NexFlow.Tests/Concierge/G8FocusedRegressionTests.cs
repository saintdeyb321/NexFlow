using System.Text.Json;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class G8FocusedRegressionTests
{
    // Versioned security contract: replay the immutable historical input without
    // changing its evaluator or authorizing its obsolete final business effect.
    [Fact]
    public async Task H06_security_v2_preserves_review_until_explicit_consent_and_commits_once()
    {
        using var stream = typeof(QualityHoldout61Tests).Assembly.GetManifestResourceStream("NexFlow.Tests.Concierge.QualityHoldout61.json")!;
        var original = JsonSerializer.Deserialize<QualityHoldout61Tests.Conversation[]>(stream)!.Single(c => c.Id == "H06");
        Assert.Equal(1, original.Turns[^1].Bookings); // Keep the original expectation as evidence.
        var f = new AssistantConversationTests.Fixture(conciergeCatalog: true, intelligentCommerce: true)
            { Now = new(2026, 10, 20, 21, 0, 0, DateTimeKind.Utc) };
        f.Services[0].PriceMinorUnits = 7500; f.UseCatalogKnowledge();
        for (var i = 0; i < original.Turns.Length - 1; i++)
        {
            var turn = original.Turns[i]; await f.HandleTurn(turn.Text, "h06-v2-" + i);
            var state = await f.Context();
            if (turn.Goal != null) Assert.Equal(turn.Goal, state.CurrentGoal);
            if (turn.Step != null) Assert.Equal(turn.Step, state.CurrentStep);
            if (turn.Date != null) Assert.Equal(turn.Date, state.TargetDate);
            Assert.Empty(f.Bookings); Assert.Empty(f.Orders);
        }
        var review = await f.Context();
        Assert.Equal(ExpectedReply.ConfirmReservation, review.CurrentQuestion!.Expected);
        var fingerprint = ConversationQuestion.Fingerprint(review);
        await f.HandleTurn(original.Turns[^1].Text, "h06-v2-ack");
        var after = await f.Context();
        Assert.Equal(review.CurrentQuestion, after.CurrentQuestion);
        Assert.Equal(fingerprint, ConversationQuestion.Fingerprint(after));
        Assert.Empty(f.Bookings); Assert.Contains("sin confirmar", f.Sent["h06-v2-ack"].Content);
        await f.HandleTurn("Confirmar", "h06-v2-consent"); await f.HandleTurn("Confirmar", "h06-v2-consent");
        var booking = Assert.Single(f.Bookings.Values);
        Assert.Equal("Elena Rojas", booking.CustomerName); Assert.Equal("sede-b", booking.LocationId);
        Assert.Equal(new DateTime(2026, 10, 22, 21, 0, 0, DateTimeKind.Utc), booking.StartTime);
        Assert.Empty(f.Orders); Assert.Equal(0, f.AiCalls);
    }
}
