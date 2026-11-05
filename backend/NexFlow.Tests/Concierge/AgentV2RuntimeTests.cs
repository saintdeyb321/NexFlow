using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.AgentV2;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.ProcessMessage;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;
using Xunit;

namespace NexFlow.Tests.Concierge;

// Injected model output proves backend behavior, never real semantic comprehension.
public sealed class AgentV2RuntimeTests
{
    [Fact]
    public async Task Legacy_journal_receipt_cannot_be_mistaken_for_an_AgentV2_response()
    {
        var c = new Case(); c.F.Plan("Consulta", AgentDecision.Information, AgentReadKind.Services);
        var old = await c.F.Turn("Consulta", "old-engine"); Assert.True(old.Publishable);
        Assert.Null((await c.F.Business.Context()).AgentV2);
        var answer = await c.Turn("Consulta", "old-engine");
        Assert.False(answer.Durable.Publishable); Assert.Empty(c.Inputs); AssertEffectsEmpty(c);
    }

    [Fact]
    public async Task Unselected_legacy_workspace_requires_no_extra_context_or_business_reads()
    {
        var settings = new Mock<IOptionsMonitor<AgentV2Options>>(); settings.SetupGet(o => o.CurrentValue).Returns(new AgentV2Options());
        var contexts = new Mock<IContextRecoveryService>(MockBehavior.Strict);
        var ingress = new AgentV2Ingress(settings.Object, Mock.Of<IAgentDemoPolicy>(), Mock.Of<IAgentV2Runtime>(), Mock.Of<IAgentTurnJournal>(),
            Mock.Of<IConversationRepository>(), contexts.Object, Mock.Of<IEntitlementService>(), Mock.Of<IOutboundMessageService>(), Mock.Of<IHumanHandoffService>(), NullLogger<AgentV2Ingress>.Instance);
        Assert.False(await ingress.TryHandleAsync(Guid.NewGuid(), "synthetic", new ProcessIncomingMessageCommand("instance", "synthetic", "Synthetic", "Consulta", "legacy", false), default));
        contexts.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(true, BusinessReadStatus.Found)]
    [InlineData(false, BusinessReadStatus.Denied)]
    public async Task Tool_provenance_rejects_foreign_workspace_and_drops_facts_from_denied_results(bool foreign, BusinessReadStatus status)
    {
        var c = new Case(); var facade = new Mock<IBusinessReadTools>();
        facade.Setup(r => r.Open(c.F.Business.Workspace)).Returns(new BusinessReadTools(c.F.Business.KnowledgeReads.Object, c.F.Business.EntitlementReads.Object).Open(c.F.Business.Workspace));
        facade.Setup(r => r.ReadAsync(It.IsAny<BusinessReadTurn>(), It.IsAny<BusinessReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessReadResult(foreign ? Guid.NewGuid() : c.F.Business.Workspace, status, "Services", null,
                [new("Offering", "Restricted", NexFlow.Application.Features.Automation.Dialogue.Contracts.VerifiedFactSource.BusinessRepository, "foreign-id")]));
        var tools = new AgentV2Tools(facade.Object, c.F.Business.OfferingReads.Object, c.F.BookingDrafts, c.F.Clock);
        var tool = tools.For(c.F.Request("Consulta", "provenance"), c.Setting, c.F.Business.Modules)["list_services"];
        var call = new AgentV2ToolCall("r", "list_services", JsonSerializer.SerializeToElement(new { }));
        if (foreign) await Assert.ThrowsAsync<InvalidOperationException>(() => tool.Execute(call, new(), default));
        else Assert.Empty((await tool.Execute(call, new(), default)).Facts);
        AssertEffectsEmpty(c);
    }

    [Fact]
    public async Task Model_invented_foreign_service_id_cannot_become_a_preparation()
    {
        var c = new Case(); c.Call("prepare_appointment", new { Service = new { Value = "other-workspace-service", Evidence = "evaluación odontológica" } });
        c.Answer("Propuesta no verificada", []);
        await c.Turn("Quiero una evaluación odontológica", "foreign-id");
        var state = await c.F.Business.Context(); Assert.Null(state.SelectedServiceId); Assert.Empty(state.PendingServiceIds);
        AssertEffectsEmpty(c);
    }
    [Fact]
    public async Task Actual_ingress_delivers_once_via_existing_outbound_and_configuration_removal_cannot_enter_legacy()
    {
        var c = new Case(channel: true); c.Read("list_services");
        await c.F.Business.HandleTurn("Consulta sintética", "ingress");
        Assert.Equal(1, c.F.WireCalls); Assert.Equal(2, c.Inputs.Count); Assert.Equal(0, c.F.Business.AiCalls);
        Assert.NotNull((await c.F.Business.Context()).AgentV2);
        await c.F.Business.HandleTurn("Consulta sintética", "ingress");
        Assert.Equal(1, c.F.WireCalls); Assert.Equal(2, c.Inputs.Count);
        c.Options.Workspaces.Clear();
        await c.F.Business.HandleTurn("Prepara una cita", "removed");
        Assert.Equal(0, c.F.Business.AiCalls); Assert.Equal(2, c.Inputs.Count); Assert.Equal(1, c.F.WireCalls);
        Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders);
    }

    [Fact]
    public async Task Unknown_delivery_is_not_reposted_and_disabled_selection_never_falls_back()
    {
        var c = new Case(channel: true); c.Read("list_services"); c.F.FailAfterWire = true;
        await c.F.Business.HandleTurn("Consulta", "uncertain");
        var id = OutboundMessageService.ResponseId(c.F.Business.Workspace, SenderType.AI, "uncertain");
        Assert.Equal(MessageStatus.UnknownDelivery, (await c.F.DemoRepository!.GetOutboundAsync(c.F.Business.Workspace, id, default))!.Value.Message.Status);
        c.F.FailAfterWire = false; await c.F.Business.HandleTurn("Consulta", "uncertain");
        Assert.Equal(1, c.F.WireCalls); Assert.Equal(2, c.Inputs.Count);
        c.Setting.Enabled = false; await c.F.Business.HandleTurn("Otra consulta", "off");
        Assert.Equal(1, c.F.WireCalls); Assert.Equal(2, c.Inputs.Count); Assert.Equal(0, c.F.Business.AiCalls);
    }

    [Fact]
    public async Task Model_selected_handoff_persists_human_ownership_before_one_ack_and_suppresses_later_automation()
    {
        var c = new Case(channel: true); c.Call("handoff", new { evidence = "una persona" });
        await c.F.Business.HandleTurn("Quiero ayuda de una persona", "human");
        Assert.Equal("Human", (await c.F.Business.Context()).Mode);
        Assert.Equal(ConversationMode.Human, (await c.F.DemoRepository!.GetConversationAsync(c.F.Business.Workspace, "chat", default))!.Mode);
        await c.F.Business.HandleTurn("Quiero ayuda de una persona", "human");
        await c.F.Business.HandleTurn("Hola otra vez", "after-human");
        Assert.Single(c.Inputs); Assert.Equal(1, c.F.WireCalls); Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders);
    }
    [Fact]
    public async Task Same_runtime_uses_authorized_workspace_context_and_different_capability_adapters()
    {
        var clinic = new Case(); var hotel = new Case("accommodation");
        clinic.F.Business.ProfileReads.Setup(p => p.GetProfileAsync(clinic.F.Business.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NexFlow.Application.Features.Business.BusinessProfileDto("Clínica sintética", "", "", "", ""));
        hotel.F.Business.ProfileReads.Setup(p => p.GetProfileAsync(hotel.F.Business.Workspace, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NexFlow.Application.Features.Business.BusinessProfileDto("Hotel sintético", "", "", "", ""));
        hotel.Setting.RoomTypeServiceIds = [hotel.F.Business.Services[0].Id];
        hotel.F.Business.Services[0].Name = "Habitación sintética";
        clinic.Read("list_services"); hotel.Read("search_room_types");
        var a = await clinic.Turn("Necesito orientación", "clinic"); var b = await hotel.Turn("Llegamos dos personas", "hotel");
        Assert.True(a.Durable.Accepted); Assert.True(b.Durable.Accepted);
        Assert.Contains("Evaluación odontológica", a.Durable.VerifiedText);
        Assert.DoesNotContain("Habitación sintética", a.Durable.VerifiedText);
        Assert.Contains("Habitación sintética", b.Durable.VerifiedText);
        Assert.DoesNotContain("Evaluación odontológica", b.Durable.VerifiedText);
        using var inputA = JsonDocument.Parse(clinic.Inputs[0]); using var inputB = JsonDocument.Parse(hotel.Inputs[0]);
        Assert.Equal("Clínica sintética", inputA.RootElement.GetProperty("Context").GetProperty("Name").GetString());
        Assert.Equal("Hotel sintético", inputB.RootElement.GetProperty("Context").GetProperty("Name").GetString());
        Assert.Contains(inputA.RootElement.GetProperty("Tools").EnumerateArray(), t => t.GetProperty("Name").GetString() == "prepare_appointment");
        Assert.DoesNotContain(inputB.RootElement.GetProperty("Tools").EnumerateArray(), t => t.GetProperty("Name").GetString() is "prepare_appointment" or "check_room_inventory" or "prepare_stay");
        Assert.All(a.Trace.Concat(b.Trace), t => Assert.Equal("AgentV2", t.Engine));
        AssertEffectsEmpty(clinic); AssertEffectsEmpty(hotel);
    }

    [Theory]
    [InlineData("list_services", "{\"workspaceId\":\"foreign\"}")]
    [InlineData("confirm_booking", "{}")]
    [InlineData("list_services", "{\"search\":123}")]
    public async Task Unregistered_tools_and_workspace_or_invalid_arguments_never_execute(string tool, string args)
    {
        var c = new Case(); c.Call(tool, JsonSerializer.Deserialize<JsonElement>(args));
        var result = await c.Turn("Consulta sintética", "bad-tool");
        Assert.False(result.Durable.Publishable); Assert.Equal("AgentV2Unavailable", result.Durable.Outcome);
        c.F.Business.KnowledgeReads.Verify(k => k.QueryAsync(It.IsAny<Guid>(), It.IsAny<BusinessKnowledgeSnapshot>(), It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>()), Times.Never);
        AssertEffectsEmpty(c);
    }

    [Fact]
    public async Task Revoked_modules_remove_tools_and_reject_in_flight_reads()
    {
        var c = new Case(); c.Call("list_services", new { });
        c.BeforeModel = () => { c.F.Business.Modules.Remove("SERVICES"); return Task.CompletedTask; };
        Assert.False((await c.Turn("Consulta", "revoked")).Durable.Publishable);
        var other = new Case(); other.F.Business.Modules.Remove("SERVICES");
        other.Read("get_business_profile"); await other.Turn("Consulta", "restricted");
        using var input = JsonDocument.Parse(other.Inputs[0]);
        Assert.DoesNotContain(input.RootElement.GetProperty("Tools").EnumerateArray(), t => t.GetProperty("Name").GetString() is "list_services" or "prepare_appointment");
        AssertEffectsEmpty(c); AssertEffectsEmpty(other);
    }

    [Fact]
    public async Task Natural_candidate_is_observable_but_self_citation_cannot_authorize_outbound_or_journaling()
    {
        var c = new Case(); c.Call("list_services", new { });
        c.Script.Enqueue(_ => Task.FromResult(JsonSerializer.Serialize(new AgentV2ModelTurn([], new("La cita ya está confirmada", ["r:0"])))));
        var answer = await c.Turn("Necesito información", "candidate");
        Assert.Equal("La cita ya está confirmada", answer.ProposedAnswer!.Text);
        Assert.False(answer.Durable.Accepted); Assert.DoesNotContain("ya está confirmada", answer.Durable.VerifiedText);
        var receipt = await c.F.Journal.ObserveAsync(c.F.Request("Necesito información", "candidate"), default);
        Assert.Null(receipt.Result!.Proposal); Assert.DoesNotContain("ya está confirmada", receipt.Result.VerifiedText);
        AssertEffectsEmpty(c);
    }

    [Fact]
    public async Task Preparation_uses_existing_slot_validation_and_keeps_its_memory_on_replay_without_commercial_effects()
    {
        var c = new Case(); c.Call("prepare_appointment", new { Service = new { Value = "Evaluación odontológica", Evidence = "evaluación odontológica" } });
        c.Answer("Preparada", []);
        var result = await c.Turn("Quiero una evaluación odontológica", "prepare");
        var state = await c.F.Business.Context();
        Assert.Null(state.SelectedServiceId); // Selection remains a candidate until a real location is chosen.
        Assert.Equal(c.F.Business.Services[0].Id, Assert.Single(state.PendingServiceIds));
        Assert.NotNull(state.AgentV2); Assert.Equal(1, state.AgentV2.Revision);
        var replay = await c.Turn("Quiero una evaluación odontológica", "prepare");
        Assert.Equal(result.Durable.VerifiedText, replay.Durable.VerifiedText); Assert.True(replay.Durable.Recovered);
        Assert.Equal(2, c.Inputs.Count); AssertEffectsEmpty(c);
    }

    [Fact]
    public async Task Concurrent_turn_replay_does_not_repeat_the_model_and_late_result_loses_CAS()
    {
        var c = new Case(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        c.Read("list_services"); c.BeforeModel = async () => { entered.TrySetResult(); await release.Task; };
        var first = c.Turn("Consulta", "race"); await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var duplicate = await c.Turn("Consulta", "race"); Assert.False(duplicate.Durable.Accepted); Assert.Single(c.Inputs);
        var state = await c.F.Business.Context(); state.Mode = "Human";
        await c.F.Business.Contexts.SaveContextAsync(c.F.Business.Workspace, AssistantConversationTests.Fixture.Phone, state, default);
        release.SetResult(); var late = await first;
        Assert.False(late.Durable.Accepted); Assert.Equal("Human", (await c.F.Business.Context()).Mode); AssertEffectsEmpty(c);
    }

    [Theory]
    [InlineData("json")]
    [InlineData("timeout")]
    [InlineData("budget")]
    [InlineData("too-many-tools")]
    public async Task Invalid_provider_or_budget_result_cannot_publish_or_fall_back_to_legacy(string fault)
    {
        var c = new Case(); c.Script.Enqueue(_ => fault switch {
            "json" => Task.FromResult("not json"),
            "timeout" => Task.FromException<string>(new TimeoutException()),
            "budget" => Task.FromException<string>(new AiBudgetDeniedException("EvaluationBudget")),
            _ => Task.FromResult(JsonSerializer.Serialize(new AgentV2ModelTurn(Enumerable.Range(0, 4).Select(i => new AgentV2ToolCall("r" + i, "list_services", JsonSerializer.SerializeToElement(new { }))).ToArray(), null)))
        });
        var answer = await c.Turn("Consulta", "fault");
        Assert.False(answer.Durable.Publishable); Assert.Equal(1, answer.Durable.ModelCalls); Assert.Equal(0, answer.Durable.ToolCalls);
        AssertEffectsEmpty(c);
    }

    [Fact]
    public async Task Revision_disabled_scope_and_legacy_context_compatibility_are_fail_closed()
    {
        var c = new Case(); c.Read("get_business_profile");
        Assert.Null(JsonSerializer.Deserialize<ConversationContextDto>("{}")!.AgentV2);
        c.Setting.Enabled = false; Assert.False((await c.Turn("Consulta", "disabled")).Durable.Publishable); Assert.Empty(c.Inputs);
        c.Setting.Enabled = true;
        Assert.False((await c.Runtime.EvaluateAsync(c.F.Request("Consulta", "scope"), default)).Durable.Publishable);
        await c.Turn("Consulta", "revision"); c.Setting.Revision++;
        Assert.False((await c.Turn("Consulta", "revision-next")).Durable.Publishable); Assert.Equal(2, c.Inputs.Count);
    }

    [Theory]
    [InlineData("missing", "1", false)]
    [InlineData("fact", "Invented price", false)]
    [InlineData("fact", "1", true)]
    public void Independent_verifier_rejects_invented_citations_or_prose(string id, string text, bool accepted)
    {
        var verdict = new AgentV2AnswerVerifier().Verify(new(text, [id]), [new("fact", "PriceMinorUnits", "1", "BusinessRepository")]);
        Assert.Equal(accepted, verdict.Accepted);
    }

    [Theory]
    [InlineData(AiTaskType.ComplexChat)]
    [InlineData(AiTaskType.ResponseComposition)]
    public async Task Exclusive_Groq_trial_has_no_fallback_on_429_for_conversation_tasks(AiTaskType task)
    {
        var groq = new Mock<IAiProvider>(MockBehavior.Strict); var gemini = new Mock<IAiProvider>(MockBehavior.Strict);
        groq.SetupGet(p => p.ProviderType).Returns(AiProviderType.Groq); gemini.SetupGet(p => p.ProviderType).Returns(AiProviderType.Gemini);
        groq.Setup(p => p.GenerateTextAsync("contract", "message", true, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("synthetic", null, HttpStatusCode.TooManyRequests));
        var router = new AiRouter([groq.Object, gemini.Object], NullLogger<AiRouter>.Instance, AiProviderType.Groq);
        await Assert.ThrowsAsync<HttpRequestException>(() => router.ExecuteTaskAsync(task, "contract", "message", true, default));
        groq.Verify(p => p.GenerateTextAsync("contract", "message", true, It.IsAny<CancellationToken>()), Times.Once);
        gemini.Verify(p => p.GenerateTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static void AssertEffectsEmpty(Case c)
    { Assert.Empty(c.F.Business.Bookings); Assert.Empty(c.F.Business.Orders); Assert.Empty(c.F.Business.Sent); Assert.Equal(0, c.F.Business.AiCalls); }

    internal sealed class Case
    {
        internal AgentDialogueFixture F { get; }
        internal AgentV2Workspace Setting { get; } = new() { Enabled = true, CapabilityPacks = ["appointments"] };
        internal AgentV2Options Options { get; } = new();
        internal IOptionsMonitor<AgentV2Options> Monitor { get; }
        internal AgentV2Runtime Runtime { get; }
        internal AgentV2Ingress? Ingress { get; }
        internal Queue<Func<JsonElement, Task<string>>> Script { get; } = [];
        internal List<string> Inputs { get; } = [];
        internal Queue<Func<JsonElement, Task<string>>> ReviewScript { get; } = [];
        internal List<string> ReviewInputs { get; } = [];
        internal Func<Task>? BeforeModel { get; set; }
        internal Func<Task>? BeforeReview { get; set; }
        internal Case(string pack = "appointments", IAiRouter? external = null, bool channel = false)
        {
            var ingress = new Mock<IAgentV2Ingress>();
            ingress.Setup(i => i.TryHandleAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<ProcessIncomingMessageCommand>(), It.IsAny<CancellationToken>()))
                .Returns((Guid w, string p, ProcessIncomingMessageCommand input, CancellationToken ct) => Ingress!.TryHandleAsync(w, p, input, ct));
            F = new(demoChannel: channel, agentV2Factory: channel ? _ => ingress.Object : null);
            Setting.CapabilityPacks = [pack]; Options.Workspaces.Add(F.Business.Workspace, Setting);
            var monitor = new Mock<IOptionsMonitor<AgentV2Options>>(); monitor.SetupGet(m => m.CurrentValue).Returns(Options); Monitor = monitor.Object;
            F.DemoOptions.Enabled = true; F.DemoOptions.WorkspaceId = F.Business.Workspace;
            F.DemoOptions.InstanceName = "instance"; F.DemoOptions.TestPhones = [AssistantConversationTests.Fixture.Phone];
            F.DemoOptions.OperatorAuthorization = "SYNTHETIC_DEMO_NO_COMMERCIAL_WRITES";
            var router = new Mock<IAiRouter>(MockBehavior.Strict);
            router.Setup(r => r.ExecuteTaskAsync(AiTaskType.ComplexChat, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
                .Returns(async (AiTaskType task, string system, string input, bool json, CancellationToken ct) => {
                    Inputs.Add(input); if (BeforeModel != null) await BeforeModel();
                    if (external != null) return await external.ExecuteTaskAsync(task, system, input, json, ct);
                    using var parsed = JsonDocument.Parse(input); return await Script.Dequeue()(parsed.RootElement);
                });
            router.Setup(r => r.ExecuteTaskAsync(AiTaskType.ResponseComposition, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
                .Returns(async (AiTaskType task, string system, string input, bool json, CancellationToken ct) => {
                    ReviewInputs.Add(input); if (BeforeReview != null) await BeforeReview();
                    if (external != null) return await external.ExecuteTaskAsync(task, system, input, json, ct);
                    using var parsed = JsonDocument.Parse(input); return await ReviewScript.Dequeue()(parsed.RootElement);
                });
            Runtime = new(router.Object, new AgentV2Tools(new BusinessReadTools(F.Business.KnowledgeReads.Object, F.Business.EntitlementReads.Object, F.Business.EngineReads.Object),
                F.Business.OfferingReads.Object, F.BookingDrafts, F.Clock), new AgentV2AnswerVerifier(), F.Journal, F.Business.Contexts,
                F.DemoRepository ?? (IConversationRepository)F.Conversations.Object, F.Business.EntitlementReads.Object, F.Business.ProfileReads.Object, F.Business.Recovery, F.DemoPolicy,
                F.Clock, Monitor, NullLogger<AgentV2Runtime>.Instance);
            if (channel) Ingress = new(Monitor, F.DemoPolicy, Runtime, F.Journal, F.DemoRepository!, F.Business.Contexts, F.Business.EntitlementReads.Object,
                F.DemoOutbound!, new HumanHandoffService(F.DemoRepository!, F.Business.Contexts), NullLogger<AgentV2Ingress>.Instance);
        }
        internal void Call(string name, object args) => Script.Enqueue(_ => Task.FromResult(JsonSerializer.Serialize(new AgentV2ModelTurn([new("r", name, JsonSerializer.SerializeToElement(args))], null))));
        internal void Answer(string text, string[] facts) => Script.Enqueue(_ => Task.FromResult(JsonSerializer.Serialize(new AgentV2ModelTurn([], new(text, facts)))));
        // These helpers inject both model outputs. They exercise publication
        // authority, never certify a real model's semantic judgement.
        internal void NaturalRead(string name, Func<JsonElement[], AgentV2Answer> answer)
        {
            Call(name, new { });
            Script.Enqueue(input => Task.FromResult(JsonSerializer.Serialize(new AgentV2ModelTurn([],
                answer(input.GetProperty("Results")[0].GetProperty("Facts").EnumerateArray().ToArray())))));
        }
        internal void InjectSupportedReview() => ReviewScript.Enqueue(input => {
            var draft = input.GetProperty("Review").GetProperty("Draft").GetString()!;
            var ids = System.Text.RegularExpressions.Regex.Matches(draft, @"\{\{([A-Za-z0-9_:-]+)\}\}")
                .Select(m => m.Groups[1].Value).Distinct().ToArray();
            return Task.FromResult(JsonSerializer.Serialize(new AgentV2SemanticReview("Supported", [new(draft, ids.Length == 0 ? "Question" : "Fact", ids)])));
        });
        internal async Task<AgentV2Evaluation> ConversationalTurn(string text, string source)
        {
            var response = await Turn(text, source);
            if (!F.History.Any(m => m.ExternalMessageId == source)) {
                F.History.Add(new() { Sender = SenderType.Consumer, Content = text, ExternalMessageId = source, Timestamp = F.Business.Now });
                if (response.Durable.Publishable) F.History.Add(new() { Sender = SenderType.AI, Content = response.Durable.VerifiedText!, Timestamp = F.Business.Now });
            }
            return response;
        }
        internal void Read(string name)
        {
            Call(name, new { }); Script.Enqueue(input => {
                var facts = input.GetProperty("Results")[0].GetProperty("Facts").EnumerateArray().ToArray();
                return Task.FromResult(JsonSerializer.Serialize(new AgentV2ModelTurn([], new(string.Join('\n', facts.Select(f => f.GetProperty("Value").GetString())), facts.Select(f => f.GetProperty("Id").GetString()!).ToArray()))));
            });
        }
        internal async Task<AgentV2Evaluation> Turn(string text, string source)
        {
            using var scope = AgentExperimentScope.Begin(F.Business.Workspace);
            return await Runtime.EvaluateAsync(F.Request(text, source), default);
        }
    }
}
