using Microsoft.Extensions.Options;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Application.Features.Automation.Agent;

public sealed class AgentDemoOptions
{
    public bool Enabled { get; set; }
    public Guid WorkspaceId { get; set; }
    public string InstanceName { get; set; } = "";
    public string[] TestPhones { get; set; } = [];
    public string OperatorAuthorization { get; set; } = "";
}
public interface IAgentDemoPolicy
{
    bool Protects(Guid workspace);
    bool Allows(Guid workspace, string instance, string phone);
    void DemandCommercialWrites(Guid workspace);
}
public sealed class AgentDemoPolicy : IAgentDemoPolicy
{
    private readonly Func<AgentDemoOptions> _current;
    private readonly HashSet<Guid> _protectedWorkspaces = [];
    private Guid? _initialWorkspace;
    private bool _identityChanged;
    private readonly AgentProtectionIdentity? _identity;
    // Existing manual fixture construction; production DI requires an attested identity.
    public AgentDemoPolicy(IOptions<AgentDemoOptions> options) : this(() => options.Value, null) { }
    private AgentDemoPolicy(Func<AgentDemoOptions> current, AgentProtectionIdentity? identity)
    {
        _current = current; _identity = identity;
        if (identity?.IsDemo == true) { _protectedWorkspaces.Add(identity.WorkspaceId); _initialWorkspace = identity.WorkspaceId; }
    }
    public static AgentDemoPolicy Live(IOptionsMonitor<AgentDemoOptions> options, AgentProtectionIdentity identity)
        => new(() => options.CurrentValue, identity);
    // Monotonic for this host lifetime. A reload cannot release a previously
    // protected tenant. Identity drift kills dialogue until a reviewed restart.
    private (AgentDemoOptions Options, bool Protected, bool Stable) Snapshot(Guid workspace)
    {
        var current = _current();
        lock (_protectedWorkspaces) {
            _initialWorkspace ??= current.WorkspaceId;
            if (current.WorkspaceId != Guid.Empty) _protectedWorkspaces.Add(current.WorkspaceId);
            _identityChanged |= current.WorkspaceId != _initialWorkspace.Value;
            return (current, workspace != Guid.Empty && _protectedWorkspaces.Contains(workspace), !_identityChanged);
        }
    }
    public bool Protects(Guid workspace) => Snapshot(workspace).Protected;
    public bool Allows(Guid workspace, string instance, string phone)
    {
        var (current, protects, stable) = Snapshot(workspace);
        return _identity?.IsDemo != false && protects && stable && workspace == current.WorkspaceId && current.Enabled
            && current.OperatorAuthorization == "SYNTHETIC_DEMO_NO_COMMERCIAL_WRITES"
            && !string.IsNullOrWhiteSpace(current.InstanceName) && instance == current.InstanceName
            && current.TestPhones is { Length: > 0 and <= 5 } && current.TestPhones.Contains(phone, StringComparer.Ordinal)
            && current.TestPhones.All(p => NexFlow.Application.Features.Reservations.ReservationContact.TryNormalize(p, null, out var normalized) && normalized == p);
    }
    public void DemandCommercialWrites(Guid workspace)
    {
        if (Protects(workspace)) throw new DomainException("DEMO: las operaciones comerciales están bloqueadas para este workspace.");
    }
}

// Separate from ExperimentScope. Only the authorized backend dispatcher opens it.
internal sealed class AgentDemoScope : IDisposable
{
    private static readonly AsyncLocal<AgentDemoScope?> Slot = new(); private readonly AgentDemoScope? _previous = Slot.Value;
    public Guid Workspace { get; } public string Phone { get; } public string Instance { get; }
    internal static AgentDemoScope? Current => Slot.Value;
    internal AgentDemoScope(Guid workspace, string phone, string instance) { Workspace = workspace; Phone = phone; Instance = instance; Slot.Value = this; }
    public void Dispose() => Slot.Value = _previous;
}
