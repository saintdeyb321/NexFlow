namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public sealed class AssistantRecoveryOptions
{
    // Assistant-only opt-in; licenses and historical API access remain separate.
    public HashSet<Guid> InformationalCommerceWorkspaces { get; set; } = [];
    public bool CommercePaused(Guid workspace) => workspace != Guid.Empty && InformationalCommerceWorkspaces.Contains(workspace);
    public HashSet<string> EffectiveModules(Guid workspace, IEnumerable<string> modules)
    {
        var effective = modules.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (CommercePaused(workspace)) { effective.Remove("ORDERS"); effective.Remove("REQUESTS"); }
        return effective;
    }
    public bool EnableContextualFocus { get; set; } // Opt-in; no production configuration changed.
    public int MaxClarifications { get; set; } = 3;
    public int MaxInputCharacters { get; set; } = 2000;
    public int ClientAiCallsPerMinute { get; set; } = 6;
    public int WorkspaceAiCallsPerMinute { get; set; } = 60;
    public int ContextExpiryHours { get; set; } = 24;
}
