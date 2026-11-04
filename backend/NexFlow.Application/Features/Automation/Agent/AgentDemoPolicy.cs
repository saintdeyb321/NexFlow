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
    public AgentDemoPolicy(IOptions<AgentDemoOptions> options) : this(() => options.Value) { }
    private AgentDemoPolicy(Func<AgentDemoOptions> current) => _current = current;
    public static AgentDemoPolicy Live(IOptionsMonitor<AgentDemoOptions> options) => new(() => options.CurrentValue);
    // Protection remains when Enabled is killed. Remove the protected workspace
    // only through a separate operator decision, never from a customer turn.
    public bool Protects(Guid workspace) => workspace != Guid.Empty && workspace == _current().WorkspaceId;
    public bool Allows(Guid workspace, string instance, string phone)
    {
        var current = _current();
        return workspace != Guid.Empty && workspace == current.WorkspaceId && current.Enabled
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
