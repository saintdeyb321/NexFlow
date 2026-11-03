using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Google.Cloud.Firestore;
using Microsoft.Extensions.Options;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.AI;
using NexFlow.Application.Features.AI.Router;

namespace NexFlow.Infrastructure.Persistence.Firestore;

// Quota admission only, never business effects. All replicas share one atomic
// account/project pool. Reservations are charged even on uncertainty or failure.
public sealed class FirestoreAiRequestAdmission(FirestoreDb db, IOptions<AiQuotaOptions> settings, IClock clock) : IAiRequestAdmission
{
    private AiQuotaPool Pool(AiProviderType provider, string model)
    {
        var matches = settings.Value.Pools.Where(p => p.Provider == provider && p.Models.Contains(model, StringComparer.Ordinal)).ToArray();
        if (matches.Length != 1) throw new AiBudgetDeniedException("UnknownQuota");
        var p = matches[0];
        if (string.IsNullOrWhiteSpace(p.Key) || p.Key.Length > 128 || p.RequestsPerMinute is not (> 0 and <= 1000)
            || p.RequestsPerDay is not > 0 || p.TokensPerMinute is not > 0 || p.TokensPerDay is not > 0
            || p.SafetyMargin is < 0 or >= 1 || !double.IsFinite(p.SafetyMargin)
            || p.WorkspaceShare is <= 0 or > .5 || !double.IsFinite(p.WorkspaceShare) || p.TokenReservationPerAttempt <= 0
            || string.IsNullOrWhiteSpace(p.ResetTimeZone)
            || settings.Value.Pools.Count(x => x.Provider == provider && x.Key == p.Key) != 1)
            throw new AiBudgetDeniedException("UnknownQuota");
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(p.ResetTimeZone); }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException) { throw new AiBudgetDeniedException("UnknownResetWindow"); }
        return p;
    }
    private DocumentReference Reference(AiQuotaPool pool) => db.Collection("ai-provider-budgets").Document(Hash($"{pool.Provider}:{pool.Key}"));
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private DateTime Now() => clock.UtcNow.Kind == DateTimeKind.Utc ? clock.UtcNow : throw new AiBudgetDeniedException("InvalidClock");
    public async Task AdmitAsync(AiProviderType provider, string model, int payloadBytes, int? maxOutputTokens, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); if (!settings.Value.Enabled) return;
        var pool = Pool(provider, model);
        if (AiTelemetry.Workspace is not { } workspace || workspace == Guid.Empty) throw new AiBudgetDeniedException("MissingWorkspace");
        if (payloadBytes < 0 || maxOutputTokens is not > 0
            || (long)payloadBytes + maxOutputTokens.Value + 2048 > pool.TokenReservationPerAttempt)
            throw new AiBudgetDeniedException("UnboundedTokenEnvelope");
        var now = Now(); var identity = Hash(workspace.ToString("N")); var reference = Reference(pool);
        var day = TimeZoneInfo.ConvertTimeFromUtc(now, TimeZoneInfo.FindSystemTimeZoneById(pool.ResetTimeZone)).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var reason = await db.RunTransactionAsync(async tx => {
            var snapshot = await tx.GetSnapshotAsync(reference, ct);
            var state = snapshot.Exists ? JsonSerializer.Deserialize<Budget>(snapshot.GetValue<string>("budgetJson"))! : new Budget();
            if (state.LastSeen > now) return "ClockBehind";
            if (state.BlockedUntil > now) return "ProviderCooldown";
            if (string.CompareOrdinal(state.Day, day) > 0) return "ClockBehind";
            if (state.Day != day) { state.Day = day; state.DailyRequests = 0; state.DailyTokens = 0; state.Workspaces.Clear(); }
            state.Minute.RemoveAll(a => a.At <= now.AddMinutes(-1));
            var own = state.Workspaces.GetValueOrDefault(identity) ?? new Usage();
            long Limit(long value, bool fair = false) => (long)Math.Floor(value * (1 - pool.SafetyMargin) * (fair ? pool.WorkspaceShare : 1));
            var tokens = pool.TokenReservationPerAttempt;
            var recent = state.Minute.Where(a => a.Workspace == identity).ToArray();
            if (state.Minute.Count + 1 > Limit(pool.RequestsPerMinute!.Value) || state.DailyRequests + 1 > Limit(pool.RequestsPerDay!.Value)
                || state.Minute.Sum(a => (long)a.Tokens) + tokens > Limit(pool.TokensPerMinute!.Value) || state.DailyTokens + tokens > Limit(pool.TokensPerDay!.Value)) return "GlobalQuota";
            if (recent.Length + 1 > Limit(pool.RequestsPerMinute.Value, true) || own.Requests + 1 > Limit(pool.RequestsPerDay.Value, true)
                || recent.Sum(a => (long)a.Tokens) + tokens > Limit(pool.TokensPerMinute.Value, true) || own.Tokens + tokens > Limit(pool.TokensPerDay.Value, true)) return "WorkspaceShare";
            if (!state.Workspaces.ContainsKey(identity) && state.Workspaces.Count >= 1000) return "WorkspaceCapacity";
            own.Requests++; own.Tokens += tokens; state.Workspaces[identity] = own;
            state.DailyRequests++; state.DailyTokens += tokens; state.LastSeen = now;
            state.Minute.Add(new() { At = now, Workspace = identity, Tokens = tokens });
            tx.Set(reference, new Dictionary<string, object> { ["budgetJson"] = JsonSerializer.Serialize(state) });
            return "Admitted";
        }, cancellationToken: ct);
        AiTelemetry.Admission(provider, model, reason);
        if (reason != "Admitted") throw new AiBudgetDeniedException(reason);
    }
    public async Task ObserveAsync(AiProviderType provider, string model, HttpResponseMessage response, CancellationToken ct)
    {
        if (!settings.Value.Enabled || response.StatusCode != HttpStatusCode.TooManyRequests) return;
        var pool = Pool(provider, model); var now = Now(); var reference = Reference(pool);
        var retry = response.Headers.RetryAfter;
        var until = retry?.Date?.UtcDateTime ?? now.Add(retry?.Delta ?? TimeSpan.FromMinutes(1));
        if (until <= now) until = now.AddMinutes(1);
        await db.RunTransactionAsync(async tx => {
            var snapshot = await tx.GetSnapshotAsync(reference, ct);
            var state = snapshot.Exists ? JsonSerializer.Deserialize<Budget>(snapshot.GetValue<string>("budgetJson"))! : new Budget();
            if (until > state.BlockedUntil) state.BlockedUntil = until;
            tx.Set(reference, new Dictionary<string, object> { ["budgetJson"] = JsonSerializer.Serialize(state) });
        }, cancellationToken: ct);
    }
    private sealed class Usage { public long Requests { get; set; } public long Tokens { get; set; } }
    private sealed class Attempt { public DateTime At { get; set; } public string Workspace { get; set; } = ""; public int Tokens { get; set; } }
    private sealed class Budget {
        public string Day { get; set; } = ""; public DateTime LastSeen { get; set; } public DateTime BlockedUntil { get; set; }
        public long DailyRequests { get; set; } public long DailyTokens { get; set; }
        public Dictionary<string, Usage> Workspaces { get; set; } = []; public List<Attempt> Minute { get; set; } = [];
    }
}
