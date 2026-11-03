using System.Text.Json;
using NexFlow.Application.Abstractions.Cache;
using NexFlow.Application.Features.Automation.Agent;
using NexFlow.Tests.Concierge;
using Xunit;

namespace NexFlow.Tests;

public sealed class AgentTurnJournalTests
{
    [Fact]
    public async Task Lease_cannot_substitute_a_different_context_generation_and_no_raw_input_is_stored()
    {
        var f = new AgentDialogueFixture(); var request = f.Request("synthetic-private-input-not-for-storage", "fence");
        var origin = await f.Business.Context(); var started = await f.Journal.BeginAsync(request, origin, f.Options, default);
        var payload = f.Business.Firestore.Read(started.Lease!.Key.Split("/documents/").Last())!.Fields["resultJson"].StringValue;
        Assert.DoesNotContain(request.Text, payload); Assert.DoesNotContain(request.ClientKey, payload);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.CompleteAsync(request, started.Lease with { ContextVersion = "forged" }, origin,
            new(true, "Prepared", null, [], 1, 0, VerifiedText: "No debe persistirse"), default));
        Assert.Equal(AgentJournalStatus.Started, (await f.Journal.ObserveAsync(request, default)).Status);
        Assert.Empty(f.Inputs);
    }
    [Theory]
    [InlineData("Version", 99)]
    [InlineData("Status", 99)]
    public async Task Unknown_record_schema_or_status_is_rejected_without_reopening_the_claim(string field, int number)
    {
        var f = new AgentDialogueFixture(); var request = f.Request("Consulta", "corrupt");
        var origin = await f.Business.Context(); var claim = await f.Journal.BeginAsync(request, origin, f.Options, default);
        var path = claim.Lease!.Key.Split("/documents/").Last();
        var doc = f.Business.Firestore.Read(path)!;
        var json = System.Text.Json.Nodes.JsonNode.Parse(doc.Fields["resultJson"].StringValue)!; json[field] = number;
        f.Business.Firestore.Seed(path, new Dictionary<string, Google.Cloud.Firestore.V1.Value> {
            ["resultJson"] = new() { StringValue = json.ToJsonString() } });
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.ObserveAsync(request, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Journal.BeginAsync(request, origin, f.Options, default));
        Assert.Empty(f.Inputs);
    }

    [Fact]
    public async Task Stable_keys_do_not_alias_when_identifiers_contain_separators()
    {
        var f = new AgentDialogueFixture(); var origin = await f.Business.Context();
        var a = f.Request("Consulta", "c") with { ConversationId = "a:b" };
        var b = f.Request("Consulta", "b:c") with { ConversationId = "a" };
        Assert.NotNull((await f.Journal.BeginAsync(a, origin, f.Options, default)).Lease);
        Assert.NotNull((await f.Journal.BeginAsync(b, origin, f.Options, default)).Lease);
    }

    [Fact]
    public async Task Prepared_result_and_context_commit_atomically_and_replay_has_no_new_owner()
    {
        var f = new AgentDialogueFixture(); var request = f.Request("Consulta", "atomic"); var origin = await f.Business.Context();
        var begin = await f.Journal.BeginAsync(request, origin, f.Options, default);
        var next = JsonSerializer.Deserialize<ConversationContextDto>(JsonSerializer.Serialize(origin))!; next.CurrentGoal = "RESERVATION";
        var result = new AgentTurnResult(true, "Prepared", null, [], 1, 1, VerifiedText: "Preparación sin registrar una cita.");
        var completed = await f.Journal.CompleteAsync(request, begin.Lease!, next, result, default);
        Assert.Equal(AgentJournalStatus.Prepared, completed.Status);
        var state = await f.Business.Context(); Assert.Equal(completed.AppliedVersion, state.StateVersion); Assert.Equal("RESERVATION", state.CurrentGoal);
        var replay = await f.Journal.BeginAsync(request, state, f.Options, default);
        Assert.Null(replay.Lease); Assert.Equal(result.VerifiedText, replay.Result!.VerifiedText);
    }

    [Theory]
    [InlineData("before-claim")]
    [InlineData("claim-ack")]
    [InlineData("before-prepare")]
    [InlineData("prepare-ack")]
    public async Task Crash_boundaries_never_reexecute_a_claim_and_recover_only_committed_results(string stage)
    {
        var f = new AgentDialogueFixture(); const string text = "Quiero reservar"; f.Plan(text, AgentDecision.PrepareReservation);
        bool Match(Google.Cloud.Firestore.V1.CommitRequest request, string status) => request.Writes.Any(w => w.Update.Name.Contains("/agentTurnResults/")
            && w.Update.Fields.TryGetValue("resultJson", out var json) && json.StringValue.Contains("\"Status\":" + status));
        if (stage is "before-claim" or "before-prepare") f.Business.Firestore.BeforeCommit = request =>
            Match(request, stage == "before-claim" ? "0" : "1") ? new IOException("synthetic precommit failure") : null;
        else f.Business.Firestore.AfterCommit = request => Match(request, stage == "claim-ack" ? "0" : "1") ? new IOException("synthetic lost ACK") : null;
        var first = await f.Turn(text, "failure"); var calls = f.Inputs.Count;
        f.Business.Firestore.BeforeCommit = null; f.Business.Firestore.AfterCommit = null;
        if (stage == "before-claim") { Assert.Equal(0, calls); var retry = await f.Turn(text, "failure"); Assert.True(retry.Publishable, retry.Outcome); }
        else if (stage == "prepare-ack") {
            var recovered = await f.Turn(text, "failure"); Assert.True(recovered.Publishable, recovered.Outcome);
            Assert.Equal(first.VerifiedText, recovered.VerifiedText); Assert.Equal(calls, f.Inputs.Count); Assert.Equal("RESERVATION", (await f.Business.Context()).CurrentGoal);
        } else {
            Assert.False(first.Publishable); f.Business.Now = f.Business.Now.AddSeconds(31);
            var failed = await f.Turn(text, "failure"); Assert.True(failed.Publishable, failed.Outcome); Assert.False(failed.Accepted);
            Assert.Equal(calls, f.Inputs.Count); Assert.Null((await f.Business.Context()).CurrentGoal);
        }
        Assert.Empty(f.Business.Bookings); Assert.Empty(f.Business.Orders); Assert.Empty(f.Business.Sent);
    }

    [Fact]
    public async Task Result_expiration_retains_a_tombstone_and_does_not_restart_IA()
    {
        var f = new AgentDialogueFixture(); f.Options.ResultRetentionHours = 1;
        f.Plan("Consulta", AgentDecision.Information);
        var first = await f.Turn("Consulta", "retention"); Assert.True(first.Publishable);
        f.Business.Now = f.Business.Now.AddHours(2);
        var expired = await f.Turn("Consulta", "retention"); Assert.Equal("ResultExpired", expired.Outcome); Assert.True(expired.Publishable);
        Assert.Single(f.Inputs); Assert.False(expired.Accepted);
        var again = await f.Turn("Consulta", "retention"); Assert.Equal(expired.VerifiedText, again.VerifiedText); Assert.Single(f.Inputs);
    }

    [Fact]
    public async Task Concurrent_replicas_have_one_owner_and_one_durable_result()
    {
        var f = new AgentDialogueFixture(); var request = f.Request("Consulta", "parallel");
        var both = await Task.WhenAll(f.Journal.BeginAsync(request, await f.Business.Context(), f.Options, default),
            f.Journal.BeginAsync(request, await f.Business.Context(), f.Options, default));
        Assert.Single(both, b => b.Lease != null); Assert.Single(both, b => b.Lease == null && b.Status == AgentJournalStatus.Started);
    }

    [Theory]
    [InlineData("version")]
    [InlineData("human")]
    [InlineData("deadline")]
    public async Task Late_owner_cannot_commit_after_context_takeover_or_expiration(string reason)
    {
        var f = new AgentDialogueFixture(); var request = f.Request("Consulta", "late"); var origin = await f.Business.Context();
        var started = await f.Journal.BeginAsync(request, origin, f.Options, default);
        if (reason == "deadline") f.Business.Now = f.Business.Now.AddSeconds(31);
        else { var current = await f.Business.Context(); if (reason == "human") current.Mode = "Human"; else current.TargetDate = "2026-11-10";
            await f.Business.Contexts.SaveContextAsync(f.Business.Workspace, request.ClientKey, current, default); }
        var late = await f.Journal.CompleteAsync(request, started.Lease!, new ConversationContextDto { CurrentGoal = "RESERVATION" },
            new(true, "Prepared", null, [], 1, 1, VerifiedText: "No debe aplicarse"), default);
        Assert.Equal(AgentJournalStatus.Failed, late.Status); Assert.Null((await f.Business.Context()).CurrentGoal);
    }
}
