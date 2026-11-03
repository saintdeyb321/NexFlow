using System.Text.Json;
using NexFlow.Application.Features.AI.Router;
using Xunit;

namespace NexFlow.Tests.Concierge;

public sealed class AgentHttpAttemptTests
{
    [Fact]
    public async Task Two_runner_instances_reserve_one_HTTP_attempt_and_leave_uncertainty_durable()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexflow-agent-attempt-" + Guid.NewGuid().ToString("N") + ".json");
        try {
            var a = new AgentHttpAttempt(path, new string('a', 64)); var b = new AgentHttpAttempt(path, new string('a', 64));
            async Task<bool> Run(AgentHttpAttempt attempt) => await Task.Run(() => { try { attempt.Start(); return true; } catch (AiBudgetDeniedException) { return false; } });
            var results = await Task.WhenAll(Run(a), Run(b)); Assert.Single(results, r => r); Assert.Single(results, r => !r);
            using var json = JsonDocument.Parse(File.ReadAllText(path)); Assert.Equal("Started", json.RootElement.GetProperty("state").GetString());
            Assert.Throws<AiBudgetDeniedException>(() => new AgentHttpAttempt(path, new string('a', 64)).Start());
        } finally { if (File.Exists(path)) File.Delete(path); } // Only this test's unique file, never an evaluation marker.
    }

    [Fact]
    public void Partial_or_unknown_attempt_file_is_never_overwritten_or_reopened()
    {
        var path = Path.Combine(Path.GetTempPath(), "nexflow-agent-partial-" + Guid.NewGuid().ToString("N") + ".json");
        try { File.WriteAllText(path, "partial"); Assert.Throws<AiBudgetDeniedException>(() => new AgentHttpAttempt(path, new string('a', 64)).Start());
            Assert.Equal("partial", File.ReadAllText(path)); }
        finally { File.Delete(path); }
    }
}
