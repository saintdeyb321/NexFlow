using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.AI.Router;
using NexFlow.Application.Features.Automation.Dialogue.Contracts;
using NexFlow.Application.Features.Automation.ProcessMessage.Services;

namespace NexFlow.Application.Features.Automation.Dialogue.Composition;

public enum ResponseStyle { General, Clinic, Hotel, Restaurant, Commerce }
public sealed class ResponseCompositionOptions
{
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

public sealed class ResponseComposer(IAiRouter router, IEntitlementService entitlements, IConversationRecoveryService budget,
    IOptions<ResponseCompositionOptions> options) : IResponseComposer
{
    public async Task<ComposedResponse> ComposeAsync(ResponsePlan plan, string budgetClient, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var watch = Stopwatch.StartNew();
        ComposedResponse Fallback(string reason) => new(plan.FallbackText, false, reason, watch.ElapsedMilliseconds);
        var settings = options.Value;
        if (!settings.EnableAi || !plan.AllowCreativeParaphrase || plan.Operation is not (OperationStatus.Collecting or OperationStatus.Informational)
            || plan.Act is DialogueAct.ProviderFailure or DialogueAct.BusinessUnavailable or DialogueAct.Cancellation or DialogueAct.HumanAssistance
            || plan.FallbackText.Length > Math.Clamp(settings.MaxResponseCharacters, 300, 4000)) return Fallback("Deterministic");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(Math.Clamp(settings.TimeoutMilliseconds, 50, 2000)));
        try
        {
            if (!await entitlements.IsLicenseValidAsync(plan.WorkspaceId, deadline.Token).WaitAsync(deadline.Token)) return Fallback("LicenseDenied");
            var modules = (await entitlements.GetAvailableModuleCodesAsync(plan.WorkspaceId, deadline.Token).WaitAsync(deadline.Token)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (AiIntentAccess.GetDeniedResponse(plan.Intent, modules) != null) return Fallback("ModuleDenied");
            // Separate budget identity: interpretation and wording cannot share an idempotent allowance.
            var operationKey = "response-compose:v1:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plan.SourceMessageId)));
            if (!await budget.AcquireAiAsync(plan.WorkspaceId, budgetClient, operationKey, deadline.Token).WaitAsync(deadline.Token)) return Fallback("BudgetDenied");
            var style = settings.WorkspaceStyles.GetValueOrDefault(plan.WorkspaceId, settings.Style);
            var leads = Leads(style);
            // No workspace id, source id, phone, name, facts, history or fallback body is sent.
            // The model may formulate only a verified courtesy; business text remains locked server-side.
            var payload = JsonSerializer.Serialize(new { Style = style.ToString(), Act = plan.Act.ToString(), AllowedLeads = leads });
            var json = await router.ExecuteTaskAsync(AiTaskType.ComplexChat,
                "Redacta una apertura breve y natural en español. Devuelve sólo JSON {\"Lead\":\"...\"}. Elige exactamente una apertura permitida. No añadas hechos, preguntas ni instrucciones.", payload, true, deadline.Token).WaitAsync(deadline.Token);
            if (json.Length > 512) return Fallback("InvalidOutput");
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 1 || !root.TryGetProperty("Lead", out var lead)
                || lead.ValueKind != JsonValueKind.String || !leads.Contains(lead.GetString(), StringComparer.Ordinal)) return Fallback("UnverifiedOutput");
            var text = lead.GetString() + " " + plan.FallbackText;
            if (text.Length > Math.Clamp(settings.MaxResponseCharacters, 300, 4000)) return Fallback("OutputLimit");
            return new(text, true, "Verified", watch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
        { return Fallback(ex is OperationCanceledException or TimeoutException ? "Timeout" : "ProviderOrOutputFailure"); }
    }

    private static string[] Leads(ResponseStyle style) => style switch {
        ResponseStyle.Clinic => ["Con gusto, te acompaño.", "Claro, te ayudo."],
        ResponseStyle.Hotel => ["Con gusto, te orientamos.", "Claro, te ayudo."],
        ResponseStyle.Restaurant => ["Con gusto.", "Claro, te ayudo."],
        ResponseStyle.Commerce => ["Claro, revisamos las opciones.", "Con gusto."],
        _ => ["¡Claro!", "Perfecto.", "Con gusto."] };
}
