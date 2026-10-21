using System.Diagnostics;
using Microsoft.Extensions.Options;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;

namespace NexFlow.Application.Features.Automation.Dialogue.Composition;

public enum ResponseStyle { General, Clinic, Hotel, Restaurant, Commerce }
public sealed class ResponseCompositionOptions
{
    // Retained configuration compatibility: styles are now selected locally, never by a paid model.
    public bool EnableAi { get; set; }
    public ResponseStyle Style { get; set; } = ResponseStyle.General;
    public Dictionary<Guid, ResponseStyle> WorkspaceStyles { get; set; } = [];
    public int TimeoutMilliseconds { get; set; } = 800;
    public int MaxResponseCharacters { get; set; } = 3000;
}
public sealed record ComposedResponse(string Text, bool UsedAi, string Outcome, long ElapsedMilliseconds);
public interface IResponseComposer
{
    Task<ComposedResponse> ComposeAsync(ResponsePlan plan, string budgetClient, CancellationToken ct);
}

public sealed class ResponseComposer : IResponseComposer
{
    private readonly IOptions<ResponseCompositionOptions> _options;
    // Keep the public constructor compatible with phase 4. No provider, entitlement or budget I/O for courtesy selection.
    public ResponseComposer(IAiRouter router, IEntitlementService entitlements, IConversationRecoveryService budget, IOptions<ResponseCompositionOptions> options)
        => _options = options;

    public Task<ComposedResponse> ComposeAsync(ResponsePlan plan, string budgetClient, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var watch=Stopwatch.StartNew(); var settings=_options.Value;
        var text=plan.FallbackText; var styled=false;
        if (settings.EnableAi && plan.AllowCreativeParaphrase && plan.Operation is OperationStatus.Collecting or OperationStatus.Informational
            && plan.Act is not (DialogueAct.ProviderFailure or DialogueAct.BusinessUnavailable or DialogueAct.Cancellation or DialogueAct.HumanAssistance))
        {
            var style=settings.WorkspaceStyles.GetValueOrDefault(plan.WorkspaceId,settings.Style);
            var lead=style switch {
                ResponseStyle.Clinic => "Con gusto, te acompaño.", ResponseStyle.Hotel => "Con gusto, te orientamos.",
                ResponseStyle.Restaurant => "Con gusto.", ResponseStyle.Commerce => "Claro, revisamos las opciones.",
                _ => plan.Act == DialogueAct.StartOperation ? "¡Claro!" : plan.Act is DialogueAct.RequestedData or DialogueAct.MultipleData ? "Perfecto." : "Con gusto."
            };
            if (lead.Length+1+text.Length <= Math.Clamp(settings.MaxResponseCharacters,300,4000)) { text=lead+" "+text; styled=true; }
        }
        return Task.FromResult(new ComposedResponse(text,false,styled ? "LocalStyle" : "Deterministic",watch.ElapsedMilliseconds));
    }
}
