namespace NexFlow.Application.Features.Automation.ProcessMessage.Services;

public sealed class AssistantRecoveryOptions
{
    public bool EnableContextualFocus { get; set; } // Opt-in; no production configuration changed.
    public int MaxClarifications { get; set; } = 3;
    public int MaxInputCharacters { get; set; } = 2000;
    public int ClientAiCallsPerMinute { get; set; } = 6;
    public int WorkspaceAiCallsPerMinute { get; set; } = 60;
    public int ContextExpiryHours { get; set; } = 24;
}
