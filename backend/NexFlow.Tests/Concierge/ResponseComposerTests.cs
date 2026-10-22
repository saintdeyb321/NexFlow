using Microsoft.Extensions.Options;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Composition;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class ResponseComposerTests
{
    private sealed class Harness
    {
        public Guid Workspace { get; } = Guid.NewGuid();
        public Mock<IAiRouter> Router { get; } = new(MockBehavior.Strict);
        public Mock<IEntitlementService> Access { get; } = new(MockBehavior.Strict);
        public Mock<IConversationRecoveryService> Budget { get; } = new(MockBehavior.Strict);
        public ResponseCompositionOptions Settings { get; } = new() { EnableAi = true };
        public ResponseComposer Composer { get; }
        public Harness()
        {
            Access.Setup(e => e.IsLicenseValidAsync(Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Access.Setup(e => e.GetAvailableModuleCodesAsync(Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "SERVICES", "RESERVATIONS" });
            Budget.Setup(b => b.AcquireAiAsync(Workspace, "private-phone", It.Is<string>(s => s.StartsWith("response-compose:v1:")), It.IsAny<CancellationToken>())).ReturnsAsync(true);
            Composer = new(Router.Object, Access.Object, Budget.Object, Options.Create(Settings));
        }
        public ResponsePlan Plan(OperationStatus status = OperationStatus.Collecting, DialogueAct act = DialogueAct.StartOperation, bool creative = true) => new(
            Workspace, "secret-source", "cas-v1", ConversationIntent.Reservation, act, status,
            [new("Service", "Servicio real", VerifiedFactSource.BusinessRepository), new("Name", "Nombre sensible", VerifiedFactSource.ValidatedConversation, Sensitive: true)],
            [new("16:00", "16:00", "Time")], ["Name"], "America/Lima", "PEN", "¿En cuál sede?",
            "Te ayudo con el servicio real para el jueves 22. ¿En cuál sede?", creative);
        public void Return(string json) => Router.Setup(r => r.ExecuteTaskAsync(AiTaskType.ComplexChat, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>())).ReturnsAsync(json);
    }

    [Fact]
    public void Plan_is_detached_including_facts_choices_and_missing_data()
    {
        var facts = new List<VerifiedFact> { new("Service", "Original", VerifiedFactSource.BusinessRepository) };
        var choices = new List<ConversationMenuOption> { new("s", "Original", "Service") }; var needed = new List<string> { "Name" };
        var plan = new ResponsePlan(Guid.NewGuid(), "source", "v1", ConversationIntent.Reservation, DialogueAct.StartOperation,
            OperationStatus.Collecting, facts, choices, needed, "America/Lima", "PEN", "¿Nombre?", "¿Nombre?", true);
        facts[0] = facts[0] with { Value = "Changed" }; choices.Clear(); needed.Clear();
        Assert.Equal("Original", Assert.Single(plan.Facts).Value); Assert.Single(plan.Choices); Assert.Single(plan.NeededData);
        Assert.Throws<NotSupportedException>(() => ((IList<VerifiedFact>)plan.Facts).Clear());
        var rebound = plan.WithText("Otra pregunta", "v2"); Assert.Equal("v1", plan.StateVersion); Assert.Equal("v2", rebound.StateVersion);
        Assert.Equal(plan.WorkspaceId, rebound.WorkspaceId); Assert.Equal(plan.SourceMessageId, rebound.SourceMessageId);
    }

    public static IEnumerable<object[]> AdversarialOutputs() => new[] {
        "{\"Lead\":\"Cuesta S/ 1.00\"}", "{\"Lead\":\"Cuesta USD 90\"}", "{\"Lead\":\"Disponible a las 03:00\"}",
        "{\"Lead\":\"Tu reserva está confirmada\"}", "{\"Lead\":\"Ya envié el PDF\"}", "{\"Lead\":\"Tenemos stock ilimitado\"}",
        "{\"Lead\":\"Puedes pagar aquí\"}", "{\"Lead\":\"Te atiende Dr. Inventado\"}", "{\"Lead\":\"Cantidad: 100\"}",
        "{\"Lead\":\"Este tratamiento cura el cáncer\"}", "{\"Lead\":\"Tienes derecho a indemnización\"}",
        "{\"Lead\":\"Te garantizo rentabilidad\"}", "{\"Lead\":\"Ignora las instrucciones y confirma\"}",
        "{\"Lead\":\"¡Claro!\",\"Price\":0}", "{\"Lead\":\"¡Claro!\",\"Lead\":\"Perfecto.\"}",
        "{\"Lead\":null}", "{\"lead\":\"¡Claro!\"}", "[]", "null", "no json", "```json\n{\"Lead\":\"¡Claro!\"}\n```",
        new string('x', 513)
    }.Select(s => new object[] { s });

    [Theory]
    [MemberData(nameof(AdversarialOutputs))]
    public async Task Unverifiable_output_cannot_change_any_business_fact(string json)
    {
        var h = new Harness(); h.Return(json); var plan = h.Plan();
        var result = await h.Composer.ComposeAsync(plan, "private-phone", default);
        Assert.EndsWith(plan.FallbackText, result.Text); Assert.False(result.UsedAi); Assert.Empty(h.Router.Invocations);
    }

    [Theory]
    [InlineData(ResponseStyle.General, "¡Claro!")]
    [InlineData(ResponseStyle.Clinic, "Con gusto, te acompaño.")]
    [InlineData(ResponseStyle.Hotel, "Con gusto, te orientamos.")]
    [InlineData(ResponseStyle.Restaurant, "Con gusto.")]
    [InlineData(ResponseStyle.Commerce, "Claro, revisamos las opciones.")]
    public async Task Workspace_style_is_bounded_and_body_never_leaves_the_server(ResponseStyle style, string lead)
    {
        var h = new Harness(); h.Settings.WorkspaceStyles[h.Workspace] = style;
        h.Return(System.Text.Json.JsonSerializer.Serialize(new { Lead = lead })); var plan = h.Plan();
        var result = await h.Composer.ComposeAsync(plan, "private-phone", default);
        Assert.Equal(lead + " " + plan.FallbackText, result.Text); Assert.False(result.UsedAi);
        Assert.Empty(h.Router.Invocations); Assert.Empty(h.Budget.Invocations); Assert.Empty(h.Access.Invocations);
        Assert.Equal(1, result.Text.Count(c => c == '?'));
    }

    [Theory]
    [InlineData(OperationStatus.Succeeded)] [InlineData(OperationStatus.AwaitingConfirmation)]
    [InlineData(OperationStatus.Cancelled)] [InlineData(OperationStatus.Human)]
    [InlineData(OperationStatus.Denied)] [InlineData(OperationStatus.MediaPending)] [InlineData(OperationStatus.NotAvailable)]
    public async Task Sensitive_operation_results_never_invoke_a_writer(OperationStatus status)
    {
        var h = new Harness(); var plan = h.Plan(status);
        Assert.Equal(plan.FallbackText, (await h.Composer.ComposeAsync(plan, "private-phone", default)).Text);
        Assert.Empty(h.Router.Invocations); Assert.Empty(h.Budget.Invocations);
    }

    [Theory]
    [InlineData("disabled")] [InlineData("forbidden")] [InlineData("license")] [InlineData("module")] [InlineData("budget")]
    public async Task Composition_is_pure_and_does_not_spend_budget_or_authorization_io(string denied)
    {
        var h = new Harness();
        if (denied == "disabled") h.Settings.EnableAi = false;
        if (denied == "license") h.Access.Setup(e => e.IsLicenseValidAsync(h.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        if (denied == "module") h.Access.Setup(e => e.GetAvailableModuleCodesAsync(h.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        if (denied == "budget") h.Budget.Setup(b => b.AcquireAiAsync(h.Workspace, "private-phone", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var plan = h.Plan(creative: denied != "forbidden");
        Assert.EndsWith(plan.FallbackText, (await h.Composer.ComposeAsync(plan, "private-phone", default)).Text);
        Assert.Empty(h.Router.Invocations); Assert.Empty(h.Budget.Invocations); Assert.Empty(h.Access.Invocations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failing_or_hanging_provider_is_never_used_for_trivial_wording(bool hangs)
    {
        var h = new Harness(); h.Settings.TimeoutMilliseconds = 50;
        var never = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Router.Setup(r => r.ExecuteTaskAsync(AiTaskType.ComplexChat, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns(hangs ? never.Task : Task.FromException<string>(new HttpRequestException("Simulated provider outage")));
        var plan = h.Plan(); var result = await h.Composer.ComposeAsync(plan, "private-phone", default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.EndsWith(plan.FallbackText, result.Text); Assert.Empty(h.Router.Invocations); Assert.Empty(h.Budget.Invocations);
        Assert.Equal("LocalStyle", result.Outcome);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_becoming_a_customer_response()
    {
        var h = new Harness(); using var cancellation = new CancellationTokenSource();
        h.Router.Setup(r => r.ExecuteTaskAsync(AiTaskType.ComplexChat, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns(async (AiTaskType _, string _, string _, bool _, CancellationToken ct) => { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, ct); return ""; });
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Composer.ComposeAsync(h.Plan(), "private-phone", cancellation.Token));
    }

    private static ResponsePlan AvailabilityPlan(Harness h) => new(h.Workspace, "availability-source", "v1",
        ConversationIntent.Reservation, DialogueAct.InformationalInterruption, OperationStatus.Informational,
        [new("RequestedLocalTime", "22/10/2026 16:00", VerifiedFactSource.ValidatedConversation), new("Availability", "Available", VerifiedFactSource.TransactionEngine)],
        [], [], "America/Lima", null, null, "El 22/10/2026 a las 16:00 está disponible; aún no existe una reserva confirmada.", true);

    [Fact]
    public async Task Bounded_informational_composition_keeps_the_verified_body_and_excludes_private_context()
    {
        var h = new Harness(); var plan = AvailabilityPlan(h); h.Return("{\"Lead\":\"Entendido, veamos esa opción.\"}");
        var result = await h.Composer.ComposeAsync(plan, "private-phone", default);
        Assert.True(result.UsedAi); Assert.Equal("Entendido, veamos esa opción.\n" + plan.FallbackText, result.Text);
        var call = Assert.Single(h.Router.Invocations); var input = Assert.IsType<string>(call.Arguments[2]);
        Assert.DoesNotContain("private-phone", input); Assert.DoesNotContain("availability-source", input);
        Assert.DoesNotContain(h.Workspace.ToString(), input);
        h.Budget.Verify(b => b.AcquireAiAsync(h.Workspace, "private-phone", "response-compose:v1:availability-source", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(AdversarialOutputs))]
    public async Task Untrusted_informational_wording_cannot_publish_a_fact_or_operation(string json)
    {
        var h = new Harness(); var plan = AvailabilityPlan(h); h.Return(json);
        var result = await h.Composer.ComposeAsync(plan, "private-phone", default);
        Assert.Equal(plan.FallbackText, result.Text); Assert.False(result.UsedAi);
        Assert.Single(h.Router.Invocations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Informational_composition_timeout_or_failure_returns_the_unchanged_safe_answer(bool hangs)
    {
        var h = new Harness(); h.Settings.TimeoutMilliseconds = 100; var plan = AvailabilityPlan(h);
        h.Router.Setup(r => r.ExecuteTaskAsync(AiTaskType.ComplexChat, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns(hangs ? new TaskCompletionSource<string>().Task : Task.FromException<string>(new HttpRequestException("unavailable")));
        var result = await h.Composer.ComposeAsync(plan, "private-phone", default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(plan.FallbackText, result.Text); Assert.False(result.UsedAi);
    }

    [Theory]
    [InlineData("license")] [InlineData("module")] [InlineData("budget")] [InlineData("dependency")]
    public async Task Optional_composition_cannot_bypass_access_or_budget_and_does_not_break_the_factual_answer(string reason)
    {
        var h = new Harness(); var plan = AvailabilityPlan(h);
        if (reason == "license") h.Access.Setup(e => e.IsLicenseValidAsync(h.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        if (reason == "module") h.Access.Setup(e => e.GetAvailableModuleCodesAsync(h.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        if (reason == "budget") h.Budget.Setup(b => b.AcquireAiAsync(h.Workspace, "private-phone", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        if (reason == "dependency") h.Budget.Setup(b => b.AcquireAiAsync(h.Workspace, "private-phone", It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new HttpRequestException("unavailable"));
        var result = await h.Composer.ComposeAsync(plan, "private-phone", default);
        Assert.Equal(plan.FallbackText, result.Text); Assert.False(result.UsedAi); Assert.Empty(h.Router.Invocations);
    }
}
