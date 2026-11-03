using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Application.Features.Automation.Conversations;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Domain.Enums;
using Xunit;
using Xunit.Abstractions;
using NexFlow.Infrastructure.Persistence.Firestore;

namespace NexFlow.Tests.Concierge;

// Provider output is scripted JSON. This certifies orchestration/contracts, not real NLU.
public sealed class LlmFirstShadowTests(ITestOutputHelper output)
{
    private sealed class Fixture
    {
        public AssistantConversationTests.Fixture Legacy { get; } = new(conciergeCatalog: true, intelligentCommerce: true);
        public Mock<IConversationRepository> Conversations { get; } = new(MockBehavior.Strict);
        public Mock<IAiRouter> Router { get; } = new(MockBehavior.Strict);
        public Queue<string> Replies { get; } = new();
        public List<string> Inputs { get; } = [];
        public List<MessageRecord> History { get; } = [];
        public AgentShadowOptions Options { get; } = new() { Enabled = true };
        public Func<Task>? DuringModel { get; set; }
        public Fixture()
        {
            Options.SyntheticWorkspaceId = Legacy.Workspace; Legacy.UseCatalogKnowledge();
            Legacy.Services[1].Name = "Extracción dental"; Legacy.Services[1].PriceMinorUnits = 9000;
            Conversations.Setup(r => r.GetConversationAsync(Legacy.Workspace, "chat", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ConversationRecord { Id = "chat", ConsumerPhone = AssistantConversationTests.Fixture.Phone, Mode = ConversationMode.Automatic });
            Conversations.Setup(r => r.HasUnconfirmedOutboundAsync(Legacy.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<CancellationToken>())).ReturnsAsync(false);
            Conversations.Setup(r => r.GetMessagesAsync(Legacy.Workspace, "chat", 6, It.IsAny<CancellationToken>(), null, null)).ReturnsAsync(() => History.ToArray());
            Router.Setup(r => r.ExecuteTaskAsync(AiTaskType.IntentExtraction, It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
                .Returns(async (AiTaskType _, string _, string input, bool _, CancellationToken _) => {
                    Inputs.Add(input); if (DuringModel != null) await DuringModel(); return Replies.Dequeue(); });
        }
        public IConversationalAgent Agent(IAgentReadTools? tools = null)
        {
            var clock = new Mock<IClock>(); clock.SetupGet(c => c.UtcNow).Returns(() => Legacy.Now);
            return new LlmFirstShadowAgent(Router.Object, tools ?? new AgentReadTools(new BusinessReadTools(Legacy.KnowledgeReads.Object, Legacy.EntitlementReads.Object)),
                Legacy.Contexts, Conversations.Object, Legacy.EntitlementReads.Object, Legacy.Recovery, clock.Object, Microsoft.Extensions.Options.Options.Create(Options),
                new FirestoreAgentEvaluationClaims(Legacy.Firestore.Db, clock.Object));
        }
        public AgentTurnRequest Request(string text = "A la que estamos preparando", string source = "shadow")
            => new(Legacy.Workspace, "chat", AssistantConversationTests.Fixture.Phone, source, text);
        public async Task PersistEmptyContext() => await Legacy.Contexts.SaveContextAsync(Legacy.Workspace,
            AssistantConversationTests.Fixture.Phone, await Legacy.Context(), default);
        public void Reply(AgentProposal proposal) => Replies.Enqueue(JsonSerializer.Serialize(proposal,
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }));
        public async Task Scope()
        {
            await Legacy.Turn("Quiero reservar", "prepare"); await Legacy.Turn("Quiero cancelar la reserva", "cancel");
            History.Add(new() { Sender = SenderType.Consumer, Direction = "inbound", Content = "Quiero cancelar la reserva", Timestamp = Legacy.Now });
        }
    }

    [Fact]
    public async Task Legacy_scope_reference_loses_the_cancel_act_and_resumes_location_collection()
    {
        var f = new Fixture(); await f.Scope(); var state = await f.Legacy.Context();
        Assert.Equal("RESERVATION_SCOPE", state.PendingRecoveryAction);
        var response = await f.Legacy.Recovery.RecoverAsync(f.Legacy.Workspace, AssistantConversationTests.Fixture.Phone, "chat",
            new("synthetic", AssistantConversationTests.Fixture.Phone, "Synthetic", "A la que estamos preparando", "scope", false, f.Legacy.Workspace), state, f.Legacy.Modules, default);
        Assert.Equal(ConversationIntent.Reservation, response!.Interpretation!.Intent);
        Assert.Equal(ConversationDirective.None, response.Interpretation.Directive); Assert.Null(state.PendingRecoveryAction);
        await f.Legacy.Turn("A la que estamos preparando", "scope-full");
        Assert.Equal("COLLECT_LOCATION", (await f.Legacy.Context()).CurrentStep);
        Assert.Empty(f.Legacy.Bookings); Assert.Empty(f.Legacy.Orders);
    }

    [Fact]
    public async Task Shadow_keeps_cancel_reference_and_information_without_mutating_the_durable_draft()
    {
        var f = new Fixture(); await f.Scope(); var before = JsonSerializer.Serialize(await f.Legacy.Context());
        var question = (await f.Legacy.Context()).CurrentQuestion!;
        f.Reply(new(AgentDecision.ProposeDiscardDraft, "A la que estamos preparando",
            "Entendido, la cita que estamos preparando. ¿Quieres dejar de prepararla?", question.QuestionId));
        var result = await f.Agent().EvaluateAsync(f.Request(), default);
        Assert.True(result.Accepted, result.Outcome); Assert.Equal(AgentDecision.ProposeDiscardDraft, result.Proposal!.Decision);
        Assert.Equal(question.QuestionId, result.Proposal.QuestionId); Assert.False(result.Publishable); Assert.Equal(1, result.ModelCalls);
        Assert.Contains("Quiero cancelar la reserva", Assert.Single(f.Inputs));
        Assert.DoesNotContain("Ana Pérez", f.Inputs[0]); Assert.DoesNotContain(AssistantConversationTests.Fixture.Phone, f.Inputs[0]);
        Assert.Equal(before, JsonSerializer.Serialize(await f.Legacy.Context()));
        Assert.Empty(f.Legacy.Bookings); Assert.Empty(f.Legacy.Orders);
        f.Reply(new(AgentDecision.Information, "Quiero hacerme una consulta", "Puedo orientarte sobre los servicios sin cambiar lo que preparabas."));
        var information = await f.Agent().EvaluateAsync(f.Request("Quiero hacerme una consulta", "information"), default);
        Assert.True(information.Accepted); Assert.Equal(before, JsonSerializer.Serialize(await f.Legacy.Context()));
    }

    [Fact]
    public async Task A_nonliteral_need_can_read_real_services_but_never_selects_or_confirms_a_treatment()
    {
        var f = new Fixture(); await f.PersistEmptyContext(); var before = JsonSerializer.Serialize(await f.Legacy.Context());
        const string text = "Hola quiero sacarme el diente";
        f.Reply(new(AgentDecision.Information, "sacarme el diente", "", Tools: [new(AgentReadKind.Services)]));
        f.Reply(new(AgentDecision.Information, "sacarme el diente", "En el catálogo figura extracción dental. ¿Quieres que te explique lo registrado?", FactReferences: [0]));
        var result = await f.Agent().EvaluateAsync(f.Request(text), default);
        Assert.True(result.Accepted, result.Outcome); Assert.Equal(2, result.ModelCalls);
        Assert.Contains(Assert.Single(result.Reads).Facts, fact => fact.Value == "Extracción dental");
        using var afterReads = JsonDocument.Parse(f.Inputs[1]);
        Assert.Contains(afterReads.RootElement.GetProperty("Reads")[0].GetProperty("Facts").EnumerateArray(),
            fact => fact.GetProperty("Value").GetString() == "Extracción dental"); Assert.False(result.Publishable);
        Assert.Equal(before, JsonSerializer.Serialize(await f.Legacy.Context())); Assert.Empty(f.Legacy.Bookings);
    }

    [Theory]
    [InlineData("off")]
    [InlineData("foreign")]
    [InlineData("human")]
    [InlineData("replay")]
    [InlineData("uncertain")]
    public async Task Disabled_foreign_human_replay_and_unknown_delivery_never_reach_the_model(string kind)
    {
        var f = new Fixture(); var request = f.Request();
        if (kind == "off") f.Options.Enabled = false;
        if (kind == "foreign") request = request with { WorkspaceId = Guid.NewGuid() };
        if (kind == "human" || kind == "replay") { var state = await f.Legacy.Context();
            if (kind == "human") state.Mode = "Human"; else state.RecoveryLastMessageId = request.SourceMessageId;
            await f.Legacy.Contexts.SaveContextAsync(f.Legacy.Workspace, request.ClientKey, state, default); }
        if (kind == "uncertain") f.Conversations.Setup(r => r.HasUnconfirmedOutboundAsync(f.Legacy.Workspace, request.ClientKey, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var result = await f.Agent().EvaluateAsync(request, default);
        Assert.False(result.Accepted); Assert.Equal(0, result.ModelCalls); Assert.Empty(f.Inputs);
        Assert.Equal(kind switch { "off" or "foreign" => "ShadowDisabled", "human" => "HumanControl",
            "replay" => "Replay", _ => "UncertainDelivery" }, result.Outcome);
        if (kind is "off" or "foreign") f.Conversations.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("json")]
    [InlineData("tool")]
    [InlineData("fact")]
    [InlineData("question")]
    [InlineData("commit")]
    [InlineData("workspace")]
    public async Task Untrusted_json_tools_facts_questions_and_commit_commands_are_rejected(string kind)
    {
        var f = new Fixture(); await f.Scope();
        var reply = kind switch {
            "json" => "not-json", "tool" => "{\"Decision\":\"Information\",\"Evidence\":\"A la\",\"Tools\":[{\"Kind\":\"ConfirmReservation\"}]}",
            "commit" => "{\"Decision\":\"ConfirmReservation\",\"Evidence\":\"A la\"}",
            "workspace" => "{\"Decision\":\"Information\",\"Evidence\":\"A la\",\"WorkspaceId\":\"forged\"}",
            _ => JsonSerializer.Serialize(new AgentProposal(kind == "question" ? AgentDecision.ProposeDiscardDraft : AgentDecision.Information,
                "A la", "Proposal", "another-question", FactReferences: kind == "fact" ? [99] : []), new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }) };
        f.Replies.Enqueue(reply); var before = JsonSerializer.Serialize(await f.Legacy.Context());
        var result = await f.Agent().EvaluateAsync(f.Request(), default);
        Assert.False(result.Accepted); Assert.Null(result.Proposal); Assert.False(result.Publishable);
        Assert.Equal(kind == "question" ? "QuestionRejected" : kind == "fact" ? "FactReferenceRejected" : "AgentUnavailable", result.Outcome);
        if (kind is "json" or "tool" or "commit" or "workspace") Assert.Equal(NexFlow.Application.Engines.AI.AiFailureKind.InvalidResponse, result.Failure);
        Assert.Equal(before, JsonSerializer.Serialize(await f.Legacy.Context())); Assert.Empty(f.Legacy.Bookings); Assert.Empty(f.Legacy.Orders);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("human")]
    [InlineData("license")]
    [InlineData("expired-question")]
    [InlineData("uncertain")]
    public async Task Stale_snapshot_handoff_revocation_and_expired_questions_cannot_validate_a_proposal(string kind)
    {
        var f = new Fixture(); await f.Scope(); var question = (await f.Legacy.Context()).CurrentQuestion!;
        f.Reply(new(AgentDecision.ProposeDiscardDraft, "A la que estamos preparando", "¿Descartamos esta preparación?", question.QuestionId));
        if (kind == "expired-question") f.Legacy.Now = f.Legacy.Now.AddMinutes(11);
        else f.DuringModel = async () => {
            if (kind == "license") f.Legacy.Modules.Remove("SERVICES");
            else if (kind == "uncertain") f.Conversations.Setup(r => r.HasUnconfirmedOutboundAsync(f.Legacy.Workspace, AssistantConversationTests.Fixture.Phone, It.IsAny<CancellationToken>())).ReturnsAsync(true);
            else { var c = await f.Legacy.Context(); if (kind == "human") c.Mode = "Human"; else c.TargetDate = "2026-12-01";
                await f.Legacy.Contexts.SaveContextAsync(f.Legacy.Workspace, AssistantConversationTests.Fixture.Phone, c, default); }
        };
        var result = await f.Agent().EvaluateAsync(f.Request(), default);
        Assert.False(result.Accepted); Assert.False(result.Publishable); Assert.Empty(f.Legacy.Bookings);
        Assert.Equal(kind == "expired-question" ? "QuestionRejected" : "StaleContext", result.Outcome);
    }

    [Theory]
    [InlineData(AgentReadKind.Services, "SERVICES")]
    [InlineData(AgentReadKind.Products, "CATALOG")]
    [InlineData(AgentReadKind.Locations, "LOCATIONS")]
    [InlineData(AgentReadKind.Hours, "BUSINESS_HOURS")]
    public async Task Read_tools_recheck_workspace_licenses_before_consulting_restricted_repositories(AgentReadKind kind, string module)
    {
        var f = new Fixture(); f.Legacy.Modules.Remove(module);
        var tools = new AgentReadTools(new BusinessReadTools(f.Legacy.KnowledgeReads.Object, f.Legacy.EntitlementReads.Object));
        var result = await tools.ReadAsync(f.Legacy.Workspace, [new(kind)], default);
        Assert.Equal(BusinessReadStatus.Denied, Assert.Single(result).Status); Assert.Empty(result[0].Facts);
        f.Legacy.KnowledgeReads.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(AgentReadKind.Services)]
    [InlineData(AgentReadKind.Products)]
    [InlineData(AgentReadKind.Locations)]
    [InlineData(AgentReadKind.Profile)]
    [InlineData(AgentReadKind.Hours)]
    public async Task Authorized_read_tools_return_only_the_requested_business_source(AgentReadKind kind)
    {
        var f = new Fixture(); var reads = new AgentReadTools(new BusinessReadTools(f.Legacy.KnowledgeReads.Object, f.Legacy.EntitlementReads.Object));
        var result = Assert.Single(await reads.ReadAsync(f.Legacy.Workspace, [new(kind)], default));
        Assert.Equal(BusinessReadStatus.Found, result.Status); Assert.NotEmpty(result.Facts);
        Assert.All(result.Facts, fact => { Assert.False(fact.Sensitive); Assert.Equal(VerifiedFactSource.BusinessRepository, fact.Source); });
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("sensitive")]
    [InlineData("source")]
    public async Task Read_tool_provenance_cannot_be_substituted_by_another_workspace_or_private_data(string kind)
    {
        var f = new Fixture(); var tools = new Mock<IBusinessReadTools>(MockBehavior.Strict);
        tools.Setup(t => t.Open(f.Legacy.Workspace)).Returns(new BusinessReadTools(f.Legacy.KnowledgeReads.Object, f.Legacy.EntitlementReads.Object).Open(f.Legacy.Workspace));
        tools.Setup(t => t.ReadAsync(It.IsAny<BusinessReadTurn>(), It.IsAny<BusinessReadRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessReadResult(kind == "foreign" ? Guid.NewGuid() : f.Legacy.Workspace,
                BusinessReadStatus.Found, kind == "source" ? "WrongSource" : "Services", null,
                [new("Service", "Synthetic", VerifiedFactSource.BusinessRepository, Sensitive: kind == "sensitive")]));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new AgentReadTools(tools.Object).ReadAsync(f.Legacy.Workspace, [new(AgentReadKind.Services)], default));
    }

    [Theory]
    [InlineData("loop")]
    [InlineData("too-many")]
    [InlineData("timeout")]
    [InlineData("cancel")]
    public async Task Tools_and_model_failures_have_a_bounded_safe_exit_without_effects(string kind)
    {
        var f = new Fixture(); const string text = "¿Qué servicios ofrecen?";
        if (kind is "loop" or "too-many") {
            f.Reply(new(AgentDecision.Information, "servicios", "", Tools: Enumerable.Repeat(new AgentToolCall(AgentReadKind.Services), kind == "too-many" ? 4 : 1).ToArray()));
            if (kind == "loop") f.Reply(new(AgentDecision.Information, "servicios", "", Tools: [new(AgentReadKind.Products)]));
        }
        if (kind == "timeout") f.Router.Setup(r => r.ExecuteTaskAsync(It.IsAny<AiTaskType>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        using var cts = new CancellationTokenSource(); if (kind == "cancel") cts.Cancel();
        if (kind == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Agent().EvaluateAsync(f.Request(text), cts.Token));
        else {
            var result = await f.Agent().EvaluateAsync(f.Request(text), default); Assert.False(result.Accepted);
            Assert.InRange(result.ModelCalls, 1, 2); Assert.Null(result.Proposal);
            Assert.Equal(kind == "loop" ? "ToolLoopRejected" : kind == "too-many" ? "ReadPlanRejected" : "AgentUnavailable", result.Outcome);
            if (kind == "timeout") Assert.Equal(NexFlow.Application.Engines.AI.AiFailureKind.Timeout, result.Failure);
        }
        Assert.Empty(f.Legacy.Bookings); Assert.Empty(f.Legacy.Orders); Assert.Empty(f.Legacy.Sent);
    }

    [Fact]
    public async Task Eight_turn_incident_compares_old_and_shadow_routes_without_real_nlu_or_business_effects()
    {
        var f = new Fixture(); await f.PersistEmptyContext(); var driver = new WhatsAppConversationalRepairTests(output);
        string[] messages = ["Hola quiero sacarme el diente", "Quiero extraerme el diente", "Quiero reservar",
            "Quiero hacerme una consulta", "Quiero cancelar la reserva", "A la que estamos preparando", "Sí", "¿Qué servicios ofrecen?"];
        AgentDecision[] decisions = [AgentDecision.Information, AgentDecision.Information, AgentDecision.PrepareReservation,
            AgentDecision.Information, AgentDecision.ClarifyCancellationScope, AgentDecision.ProposeDiscardDraft, AgentDecision.Clarify, AgentDecision.Information];
        string[] candidates = ["Puedo revisar los servicios del negocio sin asumir un tratamiento. ¿Quieres conocerlos?",
            "Puedo consultar qué servicios figuran en el catálogo, sin darte un diagnóstico. ¿Revisamos esa información?",
            "Te ayudo a preparar una cita, sin confirmarla. ¿Qué servicio necesitas?",
            "Puedes consultar primero; conservamos lo que preparabas. ¿Qué deseas saber sobre la evaluación?",
            "¿Te refieres a la reserva que estamos preparando o a una cita registrada?",
            "Entendido: la que estamos preparando. ¿Quieres dejar de prepararla?",
            "Tu respuesta queda como propuesta de descarte en esta evaluación; no he cambiado reservas.",
            "Puedo mostrarte lo que figura en el catálogo y explicarte los servicios." ];
        var calls = 0;
        for (var i = 0; i < messages.Length; i++) {
            f.Legacy.Now = f.Legacy.Now.AddMinutes(1); var text = messages[i]; var source = "incident-" + i;
            var state = await f.Legacy.Context(); var before = JsonSerializer.Serialize(state);
            f.Reply(new(decisions[i], text, candidates[i], i == 5 ? state.CurrentQuestion?.QuestionId : null));
            var shadow = await f.Agent().EvaluateAsync(f.Request(text, "shadow-" + source), default);
            Assert.True(shadow.Accepted, shadow.Outcome); Assert.Equal(decisions[i], shadow.Proposal!.Decision); Assert.False(shadow.Publishable);
            Assert.Equal(before, JsonSerializer.Serialize(await f.Legacy.Context())); calls += shadow.ModelCalls;
            if (i == 1) {
                var router = new Mock<IAiRouter>(); router.Setup(r => r.ExecuteTaskAsync(It.IsAny<AiTaskType>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>())).ReturnsAsync("not-json");
                var interpreter = new AiInterpreter(router.Object, Mock.Of<IClock>(c => c.UtcNow == f.Legacy.Now), f.Legacy.ProfileReads.Object, Microsoft.Extensions.Logging.Abstractions.NullLogger<AiInterpreter>.Instance);
                f.Legacy.InterpretationOverride = () => interpreter.InterpretAsync(f.Legacy.Workspace, text, "", null, f.Legacy.Modules, default);
                await f.Legacy.HandleTurn(text, source);
            } else await driver.Speak(f.Legacy, text, source, i == 0 ? new AiInterpretation { Intent = ConversationIntent.ServiceQuery,
                QueryKind = OfferingQueryKind.Specific, SearchTerm = "diente", Act = DialogueAct.Information, Evidence = [new("Act", text, "Information")] } : null);
            if (i == 4) Assert.Equal("RESERVATION_SCOPE", (await f.Legacy.Context()).PendingRecoveryAction);
            if (i == 5) { Assert.Null((await f.Legacy.Context()).PendingRecoveryAction); Assert.Equal("COLLECT_LOCATION", (await f.Legacy.Context()).CurrentStep); }
            output.WriteLine($"SHADOW {source}: {shadow.Proposal.CandidateResponse}; decision={shadow.Proposal.Decision}; model_calls={shadow.ModelCalls}; publishable={shadow.Publishable}");
            f.History.Add(new() { ExternalMessageId = source, Sender = SenderType.Consumer, Content = text, Timestamp = f.Legacy.Now });
            f.History.Add(new() { Sender = SenderType.AI, Content = f.Legacy.Sent[source].Content, Timestamp = f.Legacy.Now });
        }
        Assert.Equal(8, calls); Assert.Equal(8, f.Inputs.Count); Assert.Empty(f.Legacy.Bookings); Assert.Empty(f.Legacy.Orders);
        output.WriteLine("ProviderUnavailable injected via malformed JSON solely as a diagnostic example; incident provider cause remains UNKNOWN.");
        output.WriteLine($"Shadow logical calls={calls}; input_chars_total={f.Inputs.Sum(s => s.Length)}; provider_tokens=UNKNOWN; provider_HTTP=0");
    }

    [Fact]
    public async Task Distinct_shadow_messages_charge_their_own_budget_without_business_effects()
    {
        var f = new Fixture(); await f.Scope();
        for (var i = 0; i < 6; i++) {
            f.Reply(new(AgentDecision.Information, "preparando", "Conservamos la preparación; puedo explicarte las opciones."));
            Assert.True((await f.Agent().EvaluateAsync(f.Request(source: "budget-" + i), default)).Accepted);
        }
        var denied = await f.Agent().EvaluateAsync(f.Request(source: "budget-overflow"), default);
        Assert.False(denied.Accepted); Assert.Equal(0, denied.ModelCalls); Assert.Equal(6, f.Inputs.Count);
        Assert.Equal(NexFlow.Application.Engines.AI.AiFailureKind.RateLimit, denied.Failure);
        Assert.Empty(f.Legacy.Bookings); Assert.Empty(f.Legacy.Orders);
    }

    [Fact]
    public async Task Audit_replay_after_recreating_the_agent_does_not_call_the_model_again()
    {
        var f = new Fixture(); await f.Scope();
        f.Reply(new(AgentDecision.Information, "preparando", "Puedo orientarte sin cambiar la preparación."));
        f.Reply(new(AgentDecision.Information, "preparando", "Puedo orientarte sin cambiar la preparación."));
        Assert.True((await f.Agent().EvaluateAsync(f.Request(), default)).Accepted);
        var replay = await f.Agent().EvaluateAsync(f.Request(), default);
        Assert.False(replay.Accepted); Assert.Equal("EvaluationReplay", replay.Outcome);
        Assert.Equal(0, replay.ModelCalls); Assert.Single(f.Inputs);
        Assert.Empty(f.Legacy.Bookings); Assert.Empty(f.Legacy.Orders);
    }

    [Fact]
    public async Task Audit_concurrent_duplicate_is_rejected_while_the_first_model_call_is_running()
    {
        var f = new Fixture(); await f.Scope();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.DuringModel = () => { if (f.Inputs.Count != 1) return Task.CompletedTask; entered.TrySetResult(); return release.Task; };
        f.Reply(new(AgentDecision.Information, "preparando", "Puedo orientarte sin cambiar la preparación."));
        f.Reply(new(AgentDecision.Information, "preparando", "Puedo orientarte sin cambiar la preparación."));
        var first = f.Agent().EvaluateAsync(f.Request(), default);
        AgentTurnResult duplicate;
        try { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); duplicate = await f.Agent().EvaluateAsync(f.Request(), default); }
        finally { release.TrySetResult(); }
        Assert.True((await first).Accepted); Assert.False(duplicate.Accepted);
        Assert.Equal("EvaluationReplay", duplicate.Outcome); Assert.Equal(0, duplicate.ModelCalls); Assert.Single(f.Inputs);
        Assert.Empty(f.Legacy.Bookings); Assert.Empty(f.Legacy.Orders);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("claim-ack")]
    [InlineData("claim-store")]
    public async Task Audit_failures_do_not_duplicate_model_work_or_reopen_uncertain_claims(string kind)
    {
        var f = new Fixture(); await f.Scope();
        if (kind == "provider") f.Router.Setup(r => r.ExecuteTaskAsync(It.IsAny<AiTaskType>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>())).ThrowsAsync(new TimeoutException());
        if (kind == "claim-ack") f.Legacy.Firestore.AfterCommit = request => request.Writes.Any(w => w.Update.Fields.ContainsKey("requestFingerprint")) ? new IOException("synthetic lost acknowledgment") : null;
        if (kind == "claim-store") f.Legacy.Firestore.BeforeCommit = request => request.Writes.Any(w => w.Update.Fields.ContainsKey("requestFingerprint")) ? new IOException("synthetic store failure") : null;
        var failed = await f.Agent().EvaluateAsync(f.Request(), default);
        Assert.False(failed.Accepted); Assert.Equal("AgentUnavailable", failed.Outcome);
        Assert.Equal(kind == "provider" ? 1 : 0, failed.ModelCalls);
        f.Legacy.Firestore.AfterCommit = null; f.Legacy.Firestore.BeforeCommit = null;
        f.Router.Setup(r => r.ExecuteTaskAsync(It.IsAny<AiTaskType>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"Decision\":\"Information\",\"Evidence\":\"preparando\",\"CandidateResponse\":\"Conservamos la preparación.\"}");
        var retry = await f.Agent().EvaluateAsync(f.Request(), default);
        if (kind == "claim-store") { Assert.True(retry.Accepted); Assert.Equal(1, retry.ModelCalls); }
        else { Assert.False(retry.Accepted); Assert.Equal("EvaluationReplay", retry.Outcome); Assert.Equal(0, retry.ModelCalls); }
        f.Router.Verify(r => r.ExecuteTaskAsync(It.IsAny<AiTaskType>(), It.IsAny<string>(), It.IsAny<string>(), true, It.IsAny<CancellationToken>()),
            kind == "claim-ack" ? Times.Never() : Times.Once());
        Assert.Empty(f.Legacy.Bookings); Assert.Empty(f.Legacy.Orders);
    }

    [Fact]
    public async Task Audit_changed_payload_with_same_source_is_rejected_without_another_model_call()
    {
        var f = new Fixture(); await f.Scope();
        f.Reply(new(AgentDecision.Information, "preparando", "Conservamos la preparación."));
        Assert.True((await f.Agent().EvaluateAsync(f.Request(), default)).Accepted);
        var changed = await f.Agent().EvaluateAsync(f.Request("Quiero otros datos"), default);
        Assert.False(changed.Accepted); Assert.Equal("EvaluationIdentityConflict", changed.Outcome);
        Assert.Equal(0, changed.ModelCalls); Assert.Single(f.Inputs); Assert.Empty(f.Legacy.Bookings);
    }

    [Fact]
    public void Context_snapshot_uses_the_current_question_without_patient_identity_or_unrelated_modules()
    {
        var f = new Fixture(); var c = new ConversationContextDto { StateVersion = "version", CurrentGoal = "RESERVATION", CurrentStep = "CONFIRM",
            RealCustomerName = "Private Synthetic Patient", PendingCustomerName = "Another Private Name" };
        ConversationQuestion.Remember(f.Legacy.Workspace, "previous", c, ExpectedReply.ConfirmReservation, ConversationIntent.Reservation,
            "Private summary with patient identity", f.Legacy.Now, "chat");
        var capture = AgentTurnContext.Capture(f.Request("Tengo otra pregunta"), c, [new("user", "Consulta pública")],
            ["SERVICES", "RESERVATIONS", "orders", "REQUESTS", "ADMIN"], f.Legacy.Now);
        var input = JsonSerializer.Serialize(capture);
        Assert.DoesNotContain("Private", input); Assert.DoesNotContain("Another", input);
        Assert.DoesNotContain("orders", input); Assert.DoesNotContain("REQUESTS", input); Assert.DoesNotContain("ADMIN", input);
        Assert.Equal(c.CurrentQuestion!.QuestionId, capture.Question!.Id); Assert.True(capture.Draft.HasPatientName);
    }

    [Fact]
    public async Task Revocation_during_a_read_removes_facts_before_the_agent_receives_them()
    {
        var f = new Fixture();
        f.Legacy.KnowledgeReads.Setup(k => k.QueryAsync(f.Legacy.Workspace, It.IsAny<BusinessKnowledgeSnapshot>(), It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { f.Legacy.Modules.Remove("SERVICES"); return new KnowledgeResult { Status = KnowledgeStatus.Found, Source = KnowledgeTopic.Services, Facts = "Restricted synthetic content" }; });
        var read = new AgentReadTools(new BusinessReadTools(f.Legacy.KnowledgeReads.Object, f.Legacy.EntitlementReads.Object));
        var result = Assert.Single(await read.ReadAsync(f.Legacy.Workspace, [new(AgentReadKind.Services)], default));
        Assert.Equal(BusinessReadStatus.Denied, result.Status); Assert.Empty(result.Facts);
    }
}
