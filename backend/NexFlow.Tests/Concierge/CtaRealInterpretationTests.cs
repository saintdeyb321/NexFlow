using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Domain.Enums;
using NexFlow.Infrastructure.Engines.AI;
using Xunit;
using Xunit.Abstractions;

namespace NexFlow.Tests.Concierge;

// Opt-in interpretation only. No application host, stores, flows or outbound.
[Trait("Category", "ProviderIntegration")]
public sealed class CtaRealInterpretationTests(ITestOutputHelper output)
{
    [AuthorizedProviderFact]
    public async Task Twelve_synthetic_cases_through_the_production_router_with_a_shared_http_budget()
    {
        var settings = new Dictionary<string, string?>();
        foreach (var provider in new[] { "Groq", "Gemini" })
        foreach (var field in new[] { "API_KEY", "MODEL" }) {
            var value = Environment.GetEnvironmentVariable($"NEXFLOW_TEST_{provider.ToUpperInvariant()}_{field}");
            Assert.True(!string.IsNullOrWhiteSpace(value), "BLOCKED_PROVIDER_ENV: test credentials and models required; no application-secret fallback.");
            settings[provider + (field == "MODEL" ? ":Model" : ":ApiKey")] = value;
        }
        settings["Groq:TimeoutSeconds"] = "8"; settings["Gemini:TimeoutSeconds"] = "8";
        // No alternate Gemini model configured: the existing cross-provider router
        // fallback is sufficient. Every HTTP attempt still shares the same cap.
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var directory = Path.Combine(FindRoot(), "backend", "TestResults", "CTAF4"); Directory.CreateDirectory(directory);
        // CreateNew prevents accidentally spending this sample again on a retry.
        using var lease = new FileStream(Path.Combine(directory, "provider-sample.started"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var budget = new CtaProbeBudget();
        using var groqTransport = new CtaProbeTransport(budget, new HttpClientHandler { AllowAutoRedirect = false });
        using var geminiTransport = new CtaProbeTransport(budget, new HttpClientHandler { AllowAutoRedirect = false });
        using var groqHttp = new HttpClient(groqTransport); using var geminiHttp = new HttpClient(geminiTransport);
        var actualRouter = new AiRouter([
            new GroqAiProvider(groqHttp, config, NullLogger<GroqAiProvider>.Instance),
            new GeminiAiProvider(geminiHttp, config, NullLogger<GeminiAiProvider>.Instance)], NullLogger<AiRouter>.Instance);
        using var measurements = new CtaProbeMeasurements();
        var router = new CtaObservedRouter(actualRouter);
        var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(CtaProbeCases.Now.UtcDateTime);
        var interpreter = new AiInterpreter(router, clock.Object, Mock.Of<IBusinessProfileRepository>(MockBehavior.Strict), NullLogger<AiInterpreter>.Instance);
        var rows = new List<object>(); var durations = new List<double>(); var failures = 0;
        async Task<string> ReadGit(params string[] arguments)
        {
            var identity = new ProcessStartInfo("git") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, WorkingDirectory = FindRoot() };
            foreach (var argument in arguments) identity.ArgumentList.Add(argument);
            using var git = Process.Start(identity) ?? throw new InvalidOperationException("Cannot identify source tree.");
            var value = (await git.StandardOutput.ReadToEndAsync()).Trim(); await git.WaitForExitAsync();
            Assert.Equal(0, git.ExitCode); return value;
        }
        var sourceSha = await ReadGit("rev-parse", "HEAD"); var localDiff = !string.IsNullOrEmpty(await ReadGit("status", "--short"));
        Assert.True(sourceSha.Length == 40 && sourceSha.All(Uri.IsHexDigit), "Cannot identify source SHA.");
        async Task SaveEvidence() => await File.WriteAllTextAsync(Path.Combine(directory, "provider-sample.json"), JsonSerializer.Serialize(new {
            Schema = 1, SourceSha = sourceSha, LocalDiff = localDiff,
            Route = "IntentExtraction: Groq primary, Gemini transient fallback; no forced failures or alternate task routing",
            Cases = rows, Attempts = budget.Attempts, MaxAttempts = 12, AverageMilliseconds = durations.Count > 0 ? durations.Average() : (double?)null,
            Percentiles = "NOT_REPORTED_SmallSample", ProductionEffects = 0 }));
        foreach (var item in CtaProbeCases.All()) {
            var attempts = budget.Attempts; var watch = Stopwatch.StartNew(); measurements.Reset(); router.Reset();
            if (budget.Attempts >= 12) {
                rows.Add(new { item.Id, Status = "NOT_RUN_BUDGET", item.Criterion }); failures++; continue;
            }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(18));
            AiInterpretation result;
            try { result = await interpreter.InterpretAsync(item.Text, item.Context, deadline.Token); }
            catch (OperationCanceledException) { result = new() { Intent = ConversationIntent.ProviderUnavailable, Failure = AiFailureKind.Timeout }; }
            watch.Stop();
            durations.Add(watch.Elapsed.TotalMilliseconds);
            var passed = router.JsonValid == true && result.Failure == AiFailureKind.None && !result.Confirmed && item.Accept(result);
            if (!passed) failures++;
            rows.Add(new { item.Id, Status = passed ? "PASS_SEMANTIC" : result.Failure == AiFailureKind.None ? "FAIL_SEMANTIC" : "FAIL_PROVIDER_OR_CONTRACT",
                item.Criterion, JsonValid = router.JsonValid, Failure = result.Failure.ToString(),
                Structured = CtaProbeCases.Safe(result), Attempts = budget.Attempts - attempts,
                Http = budget.Observations.Skip(attempts).ToArray(), milliseconds = watch.Elapsed.TotalMilliseconds,
                InputCharacters = router.InputCharacters, Usage = measurements.Snapshot(), Cost = "UNDETERMINED_NoVerifiedTariff", BusinessEffects = 0 });
            // Preserve already completed evidence if a subsequent request fails.
            await SaveEvidence();
        }
        await SaveEvidence();
        output.WriteLine(JsonSerializer.Serialize(new { Probe = "CTA_F4_REAL", Cases = rows, Attempts = budget.Attempts }));
        Assert.True(failures == 0, "Real interpretation gate failed or incomplete; inspect sanitized provider-sample.json. Do not rerun without a new authorized budget.");
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root required.");
    }
}

internal sealed class CtaProbeBudget
{
    private int _attempts;
    public int Attempts => Volatile.Read(ref _attempts);
    public List<object> Observations { get; } = [];
    public void Take()
    {
        // No refunds for timeout/network/uncertain responses.
        if (Interlocked.Increment(ref _attempts) > 12) {
            Interlocked.Decrement(ref _attempts); throw new InvalidOperationException("CTA sample HTTP budget exhausted.");
        }
    }
}

internal sealed class CtaProbeTransport(CtaProbeBudget budget, HttpMessageHandler inner) : DelegatingHandler(inner)
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var uri = request.RequestUri;
        if (uri == null || uri.Scheme != "https" || uri.Port != 443 || uri.Query.Length != 0 || uri.UserInfo.Length != 0
            || request.Method != HttpMethod.Post || uri.Host is not ("api.groq.com" or "generativelanguage.googleapis.com"))
            throw new InvalidOperationException("CTA probe endpoint denied.");
        budget.Take(); var watch = Stopwatch.StartNew(); int? status = null;
        try { var response = await base.SendAsync(request, ct); status = (int)response.StatusCode; return response; }
        finally { budget.Observations.Add(new { Provider = uri.Host == "api.groq.com" ? "Groq" : "Gemini", Status = status, milliseconds = watch.Elapsed.TotalMilliseconds }); }
    }
}

internal sealed class CtaObservedRouter(IAiRouter actual) : IAiRouter
{
    public bool? JsonValid { get; private set; }
    public int InputCharacters { get; private set; }
    public void Reset() { JsonValid = null; InputCharacters = 0; }
    public async Task<string> ExecuteTaskAsync(AiTaskType task, string system, string text, bool json, CancellationToken ct)
    {
        InputCharacters = system.Length + text.Length;
        var raw = await actual.ExecuteTaskAsync(task, system, text, json, ct);
        try { using var parsed = JsonDocument.Parse(raw.Replace("```json", "").Replace("```", "").Trim()); JsonValid = parsed.RootElement.ValueKind == JsonValueKind.Object; }
        catch (JsonException) { JsonValid = false; }
        return raw; // Independent production interpreter validation follows.
    }
}

internal sealed class CtaProbeMeasurements : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly Dictionary<string, long> _tokens = [];
    public CtaProbeMeasurements()
    {
        _listener.InstrumentPublished = (instrument, listener) => { if (instrument.Meter.Name == AiTelemetry.MeterName && instrument.Name == "nexflow.ai.tokens") listener.EnableMeasurementEvents(instrument); };
        _listener.SetMeasurementEventCallback<long>((_, value, tags, _) => {
            string? provider = null, kind = null;
            foreach (var tag in tags) { if (tag.Key == "provider") provider = tag.Value?.ToString(); if (tag.Key == "kind") kind = tag.Value?.ToString(); }
            if (provider is "Groq" or "Gemini" && kind is "Input" or "Output" or "Total") _tokens[provider + ":" + kind] = _tokens.GetValueOrDefault(provider + ":" + kind) + value;
        }); _listener.Start();
    }
    public void Reset() => _tokens.Clear();
    public Dictionary<string, long>? Snapshot() => _tokens.Count == 0 ? null : new(_tokens);
    public void Dispose() => _listener.Dispose();
}

internal sealed record CtaProbeCase(string Id, string Text, DialogueTurnContext Context, string Criterion, Func<AiInterpretation, bool> Accept);
internal static class CtaProbeCases
{
    public static readonly DateTimeOffset Now = new(2026, 10, 31, 14, 0, 0, TimeSpan.Zero);
    public static IEnumerable<CtaProbeCase> All()
    {
        DialogueTurnContext Context(bool booking = false, bool two = false, bool noReview = false) => new(
            Guid.Parse("11111111-1111-4111-8111-111111111111"), "synthetic-turn", noReview ? null : booking ? "RESERVATION" : "ORDER",
            noReview ? null : booking ? "COLLECT_DATE" : "ORDER_REVIEW:", "synthetic-version", null, null, Now.ToOffset(TimeSpan.FromHours(-5)), "America/Lima",
            new HashSet<string>(["CATALOG", "SERVICES", "ORDERS", "RESERVATIONS", "LOCATIONS", "BUSINESS_HOURS"])) {
                SupportsFocusSwitch = true, SuspendedOperation = noReview ? null : booking ? OperationKind.Order : OperationKind.Reservation,
                DraftItems = booking || noReview ? [] : two ? ["2 × Hilo dental", "1 × Cepillo de viaje"] : ["2 × Hilo dental"],
                ProductHints = ["Hilo dental", "Cepillo de viaje"], ServiceHints = ["Limpieza dental"], LocationHints = ["Sede de prueba"],
                ReferencedOffering = two || booking || noReview ? null : new("PRODUCT", "Hilo dental", OfferingQuestion.Facts),
                Question = noReview ? null : new(ExpectedReply.RequestedData, booking ? ConversationIntent.Reservation : ConversationIntent.Order,
                    booking ? "¿Para qué fecha deseas reservar?" : "¿Deseas cambiar la selección o revisar su envío?", null)
            };
        bool Edit(AiInterpretation i, OrderEditKind kind, int? quantity = null) => i.Intent == ConversationIntent.Order && i.OrderAction == OrderTurnKind.Edit
            && i.Items.Count == 1 && i.Items[0].Kind == kind && (!quantity.HasValue || i.Items[0].Quantity == quantity);
        yield return new("C01", "Dije si está disponible el hilo dental", Context(), "ProductQuery + stock/catalog limitation, no edit", i => i.Intent == ConversationIntent.ProductQuery && i.Question is OfferingQuestion.Stock or OfferingQuestion.Availability && i.Items.Count == 0);
        yield return new("C02", "solo uno de ese", Context(), "Set(1) on the only current referenced product", i => Edit(i, OrderEditKind.Set, 1) && i.UseOfferingReference);
        yield return new("C03", "quita el segundo", Context(two: true), "Remove only current line 2, literal ordinal evidence", i => Edit(i, OrderEditKind.Remove) && i.UseOfferingReference && i.Items[0].ReferenceIndex == 2);
        yield return new("C04", "reduce uno, no lo quites", Context(), "Decrease(1), not Remove", i => Edit(i, OrderEditKind.Decrease, 1));
        yield return new("C05", "envía solicitud", Context(), "Submit is a request for review, not consent", i => i.Intent == ConversationIntent.Order && i.OrderAction == OrderTurnKind.Submit && i.Act == DialogueAct.Resume);
        yield return new("C06", "solo estoy preguntando", Context(), "Information only, no edit/start/directive", i => i.Act is DialogueAct.Information or DialogueAct.OperationInformation && i.Items.Count == 0 && i.Directive == ConversationDirective.None);
        yield return new("C07", "quieor preparar una reseva", Context(), "Reservation preparation despite typo, no commit", i => i.Intent == ConversationIntent.Reservation && i.Act == DialogueAct.StartOperation);
        yield return new("C08", "regresemos a lo de la compra", Context(booking: true), "Resume suspended Order, no write", i => i.Intent == ConversationIntent.Order && i.Act == DialogueAct.Resume);
        yield return new("C09", "cancelar", Context(), "Cancellation with no guessed discard scope", i => i.Act == DialogueAct.Cancellation && i.Directive == ConversationDirective.CancelCurrentFlow && i.DiscardScope == null);
        yield return new("C10", "descarta solo la reserva", Context(), "Cancellation targets Suspended, separate backend decision", i => i.Act == DialogueAct.Cancellation && i.Directive == ConversationDirective.CancelCurrentFlow && i.DiscardScope == DraftDiscardScope.Suspended);
        yield return new("C11", "baja uno de la compra y cambia también la fecha de la reserva", Context(), "Clarify two cross-operation mutations", i => i.RequiresClarification);
        yield return new("C12", "confirmar", Context(noReview: true), "No model consent or server command; clarify without a review", i => !i.Confirmed && i.RequiresClarification && i.Items.Count == 0);
    }
    // Do not serialize model text, names, arbitrary spans or normalized values.
    public static object Safe(AiInterpretation i) => new {
        Intent = i.Intent.ToString(), Act = i.Act?.ToString(), Directive = i.Directive.ToString(), OrderAction = i.OrderAction?.ToString(),
        i.RequiresClarification, i.UseOfferingReference, DiscardScope = i.DiscardScope?.ToString(), ModelConsentAccepted = i.Confirmed,
        Items = i.Items.Select(item => new { Product = item.Product is "Hilo dental" or "Cepillo de viaje" ? item.Product : "[outside synthetic catalog]",
            Kind = item.Kind.ToString(), item.Quantity, item.ReferenceIndex, HasReferenceSpan = item.ReferenceText != null }),
        Evidence = i.Evidence.Select(e => new { e.Slot, HasLiteralSpan = !string.IsNullOrWhiteSpace(e.Text) })
    };
}
