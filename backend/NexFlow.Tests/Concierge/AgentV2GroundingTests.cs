using System.Text.Json;
using NexFlow.Application.Features.Automation.AgentV2;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentV2GroundingTests
{
    [Fact]
    public void Extractive_compatibility_cannot_bypass_provenance_or_publish_a_free_business_receipt()
    {
        var verifier = new AgentV2AnswerVerifier();
        Assert.False(verifier.Verify(new("Untrusted", ["x"]), [new("x", "Offering", "Untrusted", "Model")]).Accepted);
        Assert.False(verifier.Verify(new("Tu reserva fue confirmada", ["x"]), [new("x", "Faqs", "Tu reserva fue confirmada", "BusinessRepository")]).Accepted);
    }
    private static readonly AgentV2Fact[] Facts = [new("s:0", "Offering", "Consulta inicial", "BusinessRepository", "svc"),
        new("s:price", "OfferingPrice", "Consulta inicial: PEN 60.00", "BusinessRepository", "svc"),
        new("s:minor", "PriceMinorUnits", "6000", "BusinessRepository", "svc"),
        new("a:0", "AvailableAppointment", "2026-11-06 10:00 (America/Lima), disponible al consultar; todavía no reservada.", "TransactionEngine", "svc")];

    [Fact]
    public void Natural_wording_with_backend_fact_anchor_is_eligible_for_independent_review()
    {
        var answer = new AgentV2Answer("Con gusto te oriento. Podemos revisar {{s:0}}. ¿Quieres conocer más?", ["s:0"]);
        var verdict = new AgentV2AnswerVerifier().Verify(answer, [new("s:0", "Offering", "Consulta inicial", "BusinessRepository", "svc")]);
        Assert.False(verdict.Accepted); Assert.Equal("SemanticReviewRequired", verdict.Reason);
        Assert.Equal("Con gusto te oriento. Podemos revisar Consulta inicial. ¿Quieres conocer más?", verdict.Text);
    }

    [Fact]
    public async Task Independently_reviewed_natural_text_preserves_wording_and_backend_bound_price()
    {
        var answer = new AgentV2Answer("Lo vemos con calma. El precio publicado es {{s:price}}. ¿Te ayudo a prepararlo?", ["s:price"]);
        var calls = 0;
        var verdict = await new AgentV2AnswerVerifier().VerifyAsync(answer, Facts, (system, input, ct) => {
            calls++; Assert.Contains("no la reescribas", system);
            using var request = JsonDocument.Parse(input); Assert.Contains("PEN 60.00", request.RootElement.GetProperty("Rendered").GetString());
            return Task.FromResult(JsonSerializer.Serialize(new AgentV2SemanticReview("Supported", [new(answer.Text, "Fact", answer.FactIds)])));
        }, default);
        Assert.Equal(1, calls); Assert.True(verdict.Accepted);
        Assert.Equal("Lo vemos con calma. El precio publicado es Consulta inicial: PEN 60.00. ¿Te ayudo a prepararlo?", verdict.Text);
        Assert.Equal("ReviewedGroundedNarrative", verdict.Reason);
    }

    [Theory]
    [InlineData("Cuesta PEN 6.00. {{s:price}}", "s:price", "UnanchoredValue")]
    [InlineData("Cuesta $sesenta. {{s:price}}", "s:price", "UnanchoredValue")]
    [InlineData("Mañana 2026-11-07 a las 12:00. {{a:0}}", "a:0", "UnanchoredValue")]
    [InlineData("La Consulta inicial cuesta {{s:price}}", "s:price", "UnanchoredEntity")]
    [InlineData("Vale {{s:minor}}", "s:minor", "UseBoundPriceFact")]
    [InlineData("Hay {{foreign:0}}", "foreign:0", "UnknownFact")]
    [InlineData("Dato {{s:0}} y {{missing:0}}", "s:0", "AnchorMismatch")]
    [InlineData("Dato {s:0}", "", "InvalidAnchorSyntax")]
    public async Task Invalid_values_entities_or_citations_are_rejected_before_review(string text, string reference, string reason)
    {
        var calls = 0; var ids = reference.Length == 0 ? Array.Empty<string>() : [reference];
        var result = await new AgentV2AnswerVerifier().VerifyAsync(new(text, ids), Facts,
            (_, _, _) => { calls++; throw new InvalidOperationException("Must not request review"); }, default);
        Assert.False(result.Accepted); Assert.Equal(reason, result.Reason); Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData("Unsupported", "Fact", false)]
    [InlineData("Ambiguous", "Fact", false)]
    [InlineData("Supported", "Confirmation", false)]
    [InlineData("Supported", "Fact", true)]
    public async Task Omitted_claims_unverified_polarity_and_unsupported_speech_acts_cannot_publish(string decision, string kind, bool omit)
    {
        var answer = new AgentV2Answer("No está disponible {{a:0}}. Ya registré la cita.", ["a:0"]);
        var result = await new AgentV2AnswerVerifier().VerifyAsync(answer, Facts, (_, _, _) =>
            Task.FromResult(JsonSerializer.Serialize(new AgentV2SemanticReview(decision, [new(omit ? "No está disponible {{a:0}}." : answer.Text, kind, answer.FactIds)]))), default);
        Assert.False(result.Accepted); Assert.Null(result.Text);
    }

    [Fact]
    public async Task A_review_cannot_certify_a_free_business_assertion_without_an_anchor()
    {
        var result = await new AgentV2AnswerVerifier().VerifyAsync(new("La reserva ya está confirmada.", []), Facts, (_, _, _) =>
            Task.FromResult(JsonSerializer.Serialize(new AgentV2SemanticReview("Supported", [new("La reserva ya está confirmada.", "Fact", [])]))), default);
        Assert.False(result.Accepted); Assert.Equal("InvalidReviewCoverage", result.Reason);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("timeout")]
    [InlineData("429")]
    [InlineData("budget")]
    public async Task Semantic_review_failures_fail_closed_without_retries(string fault)
    {
        var calls = 0; var result = await new AgentV2AnswerVerifier().VerifyAsync(new("Podemos revisar {{s:0}}", ["s:0"]), Facts,
            (_, _, _) => { calls++; return fault switch {
                "json" => Task.FromResult("invalid json"), "timeout" => Task.FromException<string>(new TimeoutException()),
                "budget" => Task.FromException<string>(new NexFlow.Application.Features.AI.Router.AiBudgetDeniedException("EvaluationBudget")),
                _ => Task.FromException<string>(new HttpRequestException("synthetic", null, System.Net.HttpStatusCode.TooManyRequests))
            }; }, default);
        Assert.False(result.Accepted); Assert.Equal(1, calls); Assert.Equal("SemanticReviewUnavailable", result.Reason);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_through_review()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new AgentV2AnswerVerifier().VerifyAsync(new("Revisamos {{s:0}}", ["s:0"]), Facts,
            (_, _, ct) => Task.FromCanceled<string>(ct), cancel.Token));
    }

    [Fact]
    public async Task A_nonfactual_question_is_allowed_only_after_separate_review()
    {
        const string text = "Con gusto te ayudo. ¿Qué te gustaría consultar primero?";
        var verdict = await new AgentV2AnswerVerifier().VerifyAsync(new(text, []), [], (_, _, _) =>
            Task.FromResult(JsonSerializer.Serialize(new AgentV2SemanticReview("Supported", [new(text, "Question", [])]))), default);
        Assert.True(verdict.Accepted); Assert.Equal(text, verdict.Text);
    }
}
