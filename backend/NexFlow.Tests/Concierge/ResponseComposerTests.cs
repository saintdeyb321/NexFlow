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
        Assert.Equal(plan.FallbackText, result.Text); Assert.False(result.UsedAi); Assert.Single(h.Router.Invocations);
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
        Assert.Equal(lead + " " + plan.FallbackText, result.Text); Assert.True(result.UsedAi);
        var request = Assert.Single(h.Router.Invocations);
        var prompt = (string)request.Arguments[1] + (string)request.Arguments[2];
        foreach (var privateValue in new[] { "private-phone", "Nombre sensible", "Servicio real", "secret-source", h.Workspace.ToString(), "cas-v1", "16:00", "America/Lima" })
            Assert.DoesNotContain(privateValue, prompt);
        Assert.Equal(1, result.Text.Count(c => c == '?')); Assert.Single(h.Budget.Invocations);
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
    public async Task Explicit_configuration_license_module_and_budget_gate_writing(string denied)
    {
        var h = new Harness();
        if (denied == "disabled") h.Settings.EnableAi = false;
        if (denied == "license") h.Access.Setup(e => e.IsLicenseValidAsync(h.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        if (denied == "module") h.Access.Setup(e => e.GetAvailableModuleCodesAsync(h.Workspace, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        if (denied == "budget") h.Budget.Setup(b => b.AcquireAiAsync(h.Workspace, "private-phone", It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var plan = h.Plan(creative: denied != "forbidden");
        Assert.Equal(plan.FallbackText, (await h.Composer.ComposeAsync(plan, "private-phone", default)).Text);
        Assert.Empty(h.Router.Invocations);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Failing_or_hanging_writer_has_one_bounded_attempt_and_local_fallback(bool hangs)
    {
        var h = new Harness(); h.Settings.TimeoutMilliseconds = 50;
        var never = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Router.Setup(r => r.ExecuteTaskAsync(AiTaskType.ComplexChat, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns(hangs ? never.Task : Task.FromException<string>(new HttpRequestException("Simulated provider outage")));
        var plan = h.Plan(); var result = await h.Composer.ComposeAsync(plan, "private-phone", default).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(plan.FallbackText, result.Text); Assert.Single(h.Router.Invocations);
        Assert.Equal(hangs ? "Timeout" : "ProviderOrOutputFailure", result.Outcome);
    }

    [Fact]
    public async Task Caller_cancellation_propagates_instead_of_becoming_a_customer_response()
    {
        var h = new Harness(); using var cancellation = new CancellationTokenSource();
        h.Router.Setup(r => r.ExecuteTaskAsync(AiTaskType.ComplexChat, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .Returns(async (AiTaskType _, string _, string _, bool _, CancellationToken ct) => { cancellation.Cancel(); await Task.Delay(Timeout.Infinite, ct); return ""; });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.Composer.ComposeAsync(h.Plan(), "private-phone", cancellation.Token));
    }
}
