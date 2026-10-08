namespace NexFlow.Application.Common;

public static class EvolutionInstanceIdentity
{
    // Compatibility with the old connection adapter only; new names are assigned from Workspace.Id.
    public static string LegacyAlias(string name) => name.Replace("-", "").Replace(" ", "").ToLowerInvariant();
}
