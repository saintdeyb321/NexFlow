namespace NexFlow.Application.Features.Automation.Agent;

// Deployment metadata loaded/attested before clients or workers are registered.
// Not an agent option, conversation store, or value supplied by a consumer/LLM.
public sealed record AgentProtectionIdentity(Guid WorkspaceId, Guid DeploymentId, string ManifestSha256)
{
    public bool IsDemo => WorkspaceId != Guid.Empty;
    public static AgentProtectionIdentity Legacy { get; } = new(Guid.Empty, Guid.Empty, "");
}
