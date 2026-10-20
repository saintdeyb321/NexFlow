using System.Text.Json;
using NexFlow.Infrastructure.Persistence.Firestore;
using Xunit;

namespace NexFlow.Tests.Integration;

[Trait("Category", "Integration")]
[Collection("RC3 isolated stores")]
public sealed class AssistantBudgetIntegrationTests
{
    [IsolatedIntegrationFact]
    public async Task Real_emulator_budget_document_enforces_duplicates_concurrent_limits_workspace_isolation_and_windows()
    {
        await using var f = await IsolatedStores.Create();
        var workspace = await f.Workspace(false); var other = await f.Workspace(false);
        var repo = new FirestoreConversationStateRepository(f.Firestore);
        var now = IsolatedStores.Day;
        Assert.True(await repo.TryConsumeAiBudgetAsync(workspace, "client-a", "first", now, 2, 3, f.Ct));
        var reference = f.Root(workspace).Collection("conversationStates").Document("ai-budget");
        var first = await reference.GetSnapshotAsync(f.Ct);
        Assert.True(first.Exists);
        Assert.True(await repo.TryConsumeAiBudgetAsync(workspace, "client-a", "first", now, 2, 3, f.Ct));
        Assert.Equal(first.UpdateTime, (await reference.GetSnapshotAsync(f.Ct)).UpdateTime);

        var admitted = await Task.WhenAll(
            repo.TryConsumeAiBudgetAsync(workspace, "client-a", "second", now, 2, 3, f.Ct),
            repo.TryConsumeAiBudgetAsync(workspace, "client-a", "third", now, 2, 3, f.Ct));
        Assert.Single(admitted, value => value);
        Assert.True(await repo.TryConsumeAiBudgetAsync(workspace, "client-b", "fourth", now, 2, 3, f.Ct));
        Assert.False(await repo.TryConsumeAiBudgetAsync(workspace, "client-c", "fifth", now, 2, 3, f.Ct));
        using (var budget = JsonDocument.Parse((await reference.GetSnapshotAsync(f.Ct)).GetValue<string>("budgetJson")))
            Assert.Equal(3, budget.RootElement.GetProperty("Sources").GetArrayLength());
        Assert.True(await repo.TryConsumeAiBudgetAsync(other, "client-a", "first", now, 2, 3, f.Ct));
        Assert.True(await repo.TryConsumeAiBudgetAsync(workspace, "client-a", "new-window", now.AddMinutes(1), 2, 3, f.Ct));
        var next = await reference.GetSnapshotAsync(f.Ct);
        Assert.False(await repo.TryConsumeAiBudgetAsync(workspace, "client-a", "stale-window", now, 2, 3, f.Ct));
        Assert.Equal(next.UpdateTime, (await reference.GetSnapshotAsync(f.Ct)).UpdateTime);
    }
}
