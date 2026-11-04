using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using Moq;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentDemoChannelTests
{
    private static Task Turn(AgentDialogueFixture f, string text, string source) => f.Business.HandleTurn(text, source);
    private static async Task<MessageStatus> Status(AgentDialogueFixture f, string source)
        => (await f.DemoRepository!.GetOutboundAsync(f.Business.Workspace, OutboundMessageService.ResponseId(f.Business.Workspace, SenderType.AI, source), default))!.Value.Message.Status;

    [Fact]
    public async Task Authorized_DEMO_uses_the_existing_durable_outbound_without_experiment_scope_or_legacy_interpretation()
    {
        var f = new AgentDialogueFixture(demoChannel: true); Assert.False(f.Options.Enabled); Assert.Null(AgentExperimentScope.Current);
        f.Plan("Hola, ¿qué servicios ofrecen?", AgentDecision.Information, AgentReadKind.Services);
        await Turn(f, "Hola, ¿qué servicios ofrecen?", "hello-demo");
        Assert.Equal(MessageStatus.Sent, await Status(f, "hello-demo")); Assert.Equal(1, f.WireCalls); Assert.Equal(2, f.Inputs.Count);
        Assert.Contains("60.00", Assert.Single(f.WireTexts)); Assert.Equal(0, f.Business.AiCalls); Assert.Empty(f.Business.Plans);
        await Turn(f, "Hola, ¿qué servicios ofrecen?", "hello-demo"); Assert.Equal(1, f.WireCalls); Assert.Equal(2, f.Inputs.Count); NoBusiness(f);
    }

    [Theory]
    [InlineData("outbound-persist")][InlineData("before-wire")][InlineData("uncertain-wire")]
    public async Task Recovery_reuses_the_prepared_result_and_never_reposts_uncertain_delivery(string failure)
    {
        var f = new AgentDialogueFixture(demoChannel: true); f.Plan("Orientación por favor", AgentDecision.Information);
        if (failure == "outbound-persist") f.Business.Firestore.BeforeCommit = c => c.Writes.Any(w => w.Update.Name.Contains("/outboundMessages/")) ? new IOException("synthetic pre-persist failure") : null;
        if (failure == "before-wire") f.BeforeWire = () => throw new IOException("synthetic pre-transport failure");
        if (failure == "uncertain-wire") f.FailAfterWire = true;
        try { await Turn(f, "Orientación por favor", "failure"); } catch (IOException) { } catch (InvalidOperationException) { }
        var calls = f.Inputs.Count; Assert.Equal(1, calls); f.Business.Firestore.BeforeCommit = null; f.BeforeWire = null; f.FailAfterWire = false;
        await Turn(f, "Orientación por favor", "failure");
        Assert.Equal(calls, f.Inputs.Count); Assert.Equal(1, f.WireCalls);
        Assert.Equal(failure == "uncertain-wire" ? MessageStatus.UnknownDelivery : MessageStatus.Sent, await Status(f, "failure")); NoBusiness(f);
    }

    [Fact]
    public async Task Kill_switch_stops_dialogue_without_legacy_fallback_or_removing_write_protection()
    {
        var f = new AgentDialogueFixture(demoChannel: true); f.DemoOptions.Enabled = false;
        await Turn(f, "Confirma una cita y un pedido", "killed");
        Assert.Empty(f.Inputs); Assert.Equal(0, f.Business.AiCalls); Assert.Equal(0, f.WireCalls); Assert.True(f.DemoPolicy.Protects(f.Business.Workspace)); NoBusiness(f);
    }

    [Fact]
    public async Task Handoff_persists_human_mode_before_its_single_ack_without_claiming_business_notification()
    {
        var f = new AgentDialogueFixture(demoChannel: true); f.Plan("Quiero ayuda de una persona", AgentDecision.HumanAssistance);
        await Turn(f, "Quiero ayuda de una persona", "handoff");
        Assert.Equal(ConversationMode.Human, (await f.DemoRepository!.GetConversationAsync(f.Business.Workspace, "chat", default))!.Mode);
        Assert.Equal("Human", (await f.Business.Context()).Mode); Assert.Equal(1, f.WireCalls);
        await Turn(f, "Quiero ayuda de una persona", "handoff"); Assert.Equal(1, f.WireCalls); Assert.Single(f.Inputs);
        await Turn(f, "Sigue el bot", "after-human"); Assert.Single(f.Inputs); Assert.Equal(1, f.WireCalls); NoBusiness(f);
    }

    [Fact]
    public async Task Simultaneous_duplicate_has_one_model_evaluation_and_one_transport()
    {
        var f = new AgentDialogueFixture(demoChannel: true); f.Plan("Ayúdame", AgentDecision.Information);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.DuringModel = async () => { entered.TrySetResult(); await release.Task; };
        var first = Turn(f, "Ayúdame", "same"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try { await Assert.ThrowsAsync<ConcurrencyException>(() => Turn(f, "Ayúdame", "same")); } finally { release.TrySetResult(); }
        await first; await Turn(f, "Ayúdame", "same"); Assert.Single(f.Inputs); Assert.Equal(1, f.WireCalls); NoBusiness(f);
    }

    private static void NoBusiness(AgentDialogueFixture f) { Assert.Empty(f.Business.Bookings); Assert.Empty(f.Business.Orders); }

    [Fact]
    public async Task Unattributed_legacy_pending_text_cannot_be_sent_as_a_DEMO_answer()
    {
        var f = new AgentDialogueFixture(demoChannel: true); const string source = "old-pending";
        await f.DemoRepository!.PrepareOutboundAsync(f.Business.Workspace, "chat", AssistantConversationTests.Fixture.Phone, new() {
            Id = OutboundMessageService.ResponseId(f.Business.Workspace, SenderType.AI, source),
            IdempotencyKey = $"{f.Business.Workspace:N}:AI:{source}:response:1", Sender = SenderType.AI,
            Origin = MessageOrigin.NexFlowAI, Content = "Reserva confirmada por una ruta antigua", Status = MessageStatus.Pending,
            Direction = "outbound", Timestamp = DateTime.UtcNow }, default);
        f.Plan("Orientación", AgentDecision.Information);
        await Assert.ThrowsAsync<ConcurrencyException>(() => Turn(f, "Orientación", source));
        Assert.Equal(0, f.WireCalls); Assert.Equal(MessageStatus.Pending, await Status(f, source)); NoBusiness(f);
    }

    [Theory]
    [InlineData("module")][InlineData("human")]
    public async Task Prepared_but_unsent_facts_are_not_delivered_after_revocation_or_manual_takeover(string change)
    {
        var f = new AgentDialogueFixture(demoChannel: true); f.Plan("Ver servicios", AgentDecision.Information, AgentReadKind.Services);
        f.Business.Firestore.BeforeCommit = c => c.Writes.Any(w => w.Update.Name.Contains("/outboundMessages/")) ? new IOException("synthetic write failure") : null;
        await Assert.ThrowsAsync<IOException>(() => Turn(f, "Ver servicios", "late-policy"));
        f.Business.Firestore.BeforeCommit = null;
        if (change == "module") f.Business.Modules.Remove("SERVICES");
        else await new NexFlow.Application.Features.Automation.Conversations.HumanHandoffService(f.DemoRepository!, f.Business.Contexts)
            .EscalateToHumanAsync(f.Business.Workspace, "chat", HandoffReason.ManualIntervention, default);
        await Assert.ThrowsAsync<ConcurrencyException>(() => Turn(f, "Ver servicios", "late-policy"));
        Assert.Equal(2, f.Inputs.Count); Assert.Equal(0, f.WireCalls); NoBusiness(f);
    }

    [Fact]
    public async Task Handoff_ack_survives_failure_between_durable_mode_and_outbound_creation()
    {
        var f = new AgentDialogueFixture(demoChannel: true); f.Plan("Necesito atención humana", AgentDecision.HumanAssistance);
        f.Business.Firestore.BeforeCommit = c => c.Writes.Any(w => w.Update.Name.Contains("/outboundMessages/")) ? new IOException("synthetic write failure") : null;
        await Assert.ThrowsAsync<IOException>(() => Turn(f, "Necesito atención humana", "handoff-crash"));
        Assert.Equal(ConversationMode.Human, (await f.DemoRepository!.GetConversationAsync(f.Business.Workspace, "chat", default))!.Mode);
        f.Business.Firestore.BeforeCommit = null;
        await Turn(f, "Necesito atención humana", "handoff-crash");
        Assert.Single(f.Inputs); Assert.Equal(1, f.WireCalls); Assert.Equal(MessageStatus.Sent, await Status(f, "handoff-crash")); NoBusiness(f);
    }

    [Fact]
    public async Task Kill_between_ingress_and_orchestration_cannot_fall_through_to_legacy()
    {
        var f = new AgentDialogueFixture(demoChannel: true);
        f.Business.StateReads.Setup(s => s.ProcessStateAsync(f.Business.Workspace, AssistantConversationTests.Fixture.Phone,
            Moq.It.IsAny<ProcessIncomingMessageCommand>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => {
                f.DemoOptions.Enabled = false;
                return (true, new NexFlow.Application.Features.Automation.Conversations.ConversationRecord { Id = "chat", Mode = ConversationMode.Automatic }, (string?)null);
            });
        await Assert.ThrowsAsync<ConcurrencyException>(() => Turn(f, "Una consulta", "kill-inflight"));
        Assert.Empty(f.Inputs); Assert.Equal(0, f.Business.AiCalls); Assert.Equal(0, f.WireCalls); NoBusiness(f);
    }
}
