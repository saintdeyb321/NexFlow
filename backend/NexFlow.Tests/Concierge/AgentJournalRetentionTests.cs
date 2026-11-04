using System.Text.Json;
using NexFlow.Application.Features.Automation.Agent;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentJournalRetentionTests
{
    [Fact]
    public async Task Physical_compaction_removes_payload_and_keeps_the_replay_tombstone()
    {
        var f = new AgentDialogueFixture(); var request = f.Request("Synthetic input", "retention-batch");
        var origin = await f.Business.Context(); var started = await f.Journal.BeginAsync(request, origin, f.Options, default);
        await f.Journal.CompleteAsync(request, started.Lease!, null, new(true, "Prepared", null, [], 1, 0,
            VerifiedText: "synthetic-payload-to-be-removed"), default);
        f.Business.Now = f.Business.Now.AddHours(25);
        using var scope = AgentExperimentScope.Begin(f.Business.Workspace);
        var compacted = await f.Journal.CompactBatchAsync(f.Business.Workspace, null, 1, default); Assert.Equal(1, compacted.Cleared);
        var path = started.Lease!.Key.Split("/documents/").Last(); var raw = f.Business.Firestore.Read(path)!.Fields["resultJson"].StringValue;
        Assert.DoesNotContain("synthetic-payload-to-be-removed", raw); Assert.Contains("PayloadCleared", raw);
        var replay = await f.Journal.BeginAsync(request, origin, f.Options, default); Assert.Null(replay.Lease); Assert.Equal("ResultExpired", replay.Result!.Outcome);
        Assert.Equal(0, (await f.Journal.CompactBatchAsync(f.Business.Workspace, null, 1, default)).Cleared); Assert.Empty(f.Inputs);
    }

    [Fact]
    public async Task Compaction_is_bounded_scoped_and_does_not_erase_a_live_owner()
    {
        var f = new AgentDialogueFixture(); var request = f.Request("Consulta", "live-owner");
        await f.Journal.BeginAsync(request, await f.Business.Context(), f.Options, default);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Journal.CompactBatchAsync(f.Business.Workspace, null, 1, default));
        using var scope = AgentExperimentScope.Begin(f.Business.Workspace);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Journal.CompactBatchAsync(Guid.NewGuid(), null, 1, default));
        await Assert.ThrowsAsync<ArgumentException>(() => f.Journal.CompactBatchAsync(f.Business.Workspace, null, 101, default));
        Assert.Equal(0, (await f.Journal.CompactBatchAsync(f.Business.Workspace, null, 1, default)).Cleared);
        Assert.Equal(AgentJournalStatus.Started, (await f.Journal.ObserveAsync(request, default)).Status);
    }

    [Fact]
    public async Task Human_request_is_not_reported_as_notification_or_completed_handoff()
    {
        var f = new AgentDialogueFixture(); f.Plan("Necesito ayuda humana", AgentDecision.HumanAssistance);
        var reply = await f.Turn("Necesito ayuda humana", "human-request");
        Assert.Equal(AgentHandoffState.Requested, reply.Handoff); Assert.Equal("AgentHandoffRequested", (await f.Business.Context()).HandoffReason);
        Assert.Equal(0, f.Business.Handoffs); Assert.Empty(f.Business.Sent); Assert.DoesNotContain("he avisado", reply.VerifiedText!);
    }
}
