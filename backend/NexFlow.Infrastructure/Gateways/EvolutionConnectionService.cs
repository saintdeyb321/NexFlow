using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Infrastructure.Gateways;

public sealed class EvolutionConnectionService : IEvolutionConnectionService
{
    private readonly HttpClient _http;
    private readonly IWhatsAppConnectionRepository _connections;
    private readonly IClock _clock;
    private readonly ILogger<EvolutionConnectionService> _logger;
    private readonly string _baseUrl;
    private readonly string _webhookUrl;
    private readonly string _webhookKey;

    public EvolutionConnectionService(HttpClient http, IConfiguration configuration, IWhatsAppConnectionRepository connections,
        IClock clock, ILogger<EvolutionConnectionService> logger)
    {
        _http = http;
        _connections = connections;
        _clock = clock;
        _logger = logger;
        _baseUrl = configuration["Evolution:BaseUrl"]?.TrimEnd('/') ?? throw new InvalidOperationException("Evolution no configurado.");
        _webhookUrl = configuration["Evolution:WebhookUrl"] ?? string.Empty;
        _webhookKey = configuration["Evolution:WebhookKey"]?.Trim() ?? string.Empty;
        var seconds = int.TryParse(configuration["Evolution:TimeoutSeconds"], out var configured) ? configured : 8;
        _http.Timeout = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 15));
        _http.DefaultRequestHeaders.Remove("apikey");
        _http.DefaultRequestHeaders.Add("apikey", configuration["Evolution:ApiKey"] ?? string.Empty);
    }

    public async Task<WhatsAppConnectionStatus> GetStatusAsync(Guid workspaceId, bool refresh, CancellationToken ct)
    {
        var snapshot = await _connections.GetAsync(workspaceId, ct);
        if (snapshot.Connection.LogoutPending) return Present(snapshot.Connection);
        if (snapshot.Connection.OperationUntil > _clock.UtcNow) return Present(snapshot.Connection);
        if (!refresh && snapshot.Connection.ObservedAt > _clock.UtcNow.AddSeconds(-10)) return Present(snapshot.Connection);
        // A status request never creates, connects, logs out or reconfigures an external instance.
        try
        {
            var remote = await InspectAsync(workspaceId, snapshot.InstanceName, ct);
            var row = snapshot.Connection;
            var hasSession = HasSession(remote, row);
            var status = remote.State == "open" ? "CONNECTED" : row.IsLinked == true || hasSession ? "RECONNECTING"
                : row.QrExpiresAt > _clock.UtcNow && row.QrBase64 != null ? "QR_AVAILABLE"
                : row.QrExpiresAt != null ? "QR_EXPIRED" : "DISCONNECTED";
            await _connections.SaveObservationAsync(workspaceId, null, new(status, hasSession, row.QrBase64, row.QrExpiresAt), _clock.UtcNow, ct, row.PersistenceVersion);
            return Present((await _connections.GetAsync(workspaceId, ct)).Connection);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TimeoutException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            _logger.LogWarning("Evolution status unavailable for workspace {WorkspaceId}", workspaceId);
            return Present(snapshot.Connection) with { Status = "UNAVAILABLE", CanConnect = false, Message = "Evolution no está disponible. La vinculación se conserva; vuelve a consultar el estado." };
        }
    }

    public async Task<WhatsAppConnectionStatus> ConnectAsync(Guid workspaceId, CancellationToken ct)
    {
        var before = await _connections.GetAsync(workspaceId, ct);
        if (before.Connection.LogoutPending) throw new ConcurrencyException("Confirma primero la desconexión pendiente.");
        if (before.Connection.IsLinked != true && before.Connection.QrExpiresAt > _clock.UtcNow && before.Connection.QrBase64 != null)
            return Present(before.Connection); // Repeated clicks reuse the same short-lived QR.
        var operation = await _connections.AcquireAsync(workspaceId, false, _clock.UtcNow, _clock.UtcNow.AddMinutes(2), ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var snapshot = await _connections.GetAsync(workspaceId, timeout.Token);
            var remote = await InspectAsync(workspaceId, snapshot.InstanceName, timeout.Token);
            if (snapshot.Connection.IsLinked == true || HasSession(remote, snapshot.Connection))
            {
                var status = remote.State == "open" ? "CONNECTED" : "RECONNECTING";
                await _connections.SaveObservationAsync(workspaceId, operation, new(status, true), _clock.UtcNow, timeout.Token);
                // Existing credentials belong to this session. Never expose a new pairing QR or create a replacement.
                if (remote.Exists && remote.State == "connecting")
                {
                    using var restart = await _http.PutAsync(Url("instance/restart", remote.Name), null, timeout.Token);
                    await ReadSuccessAsync(restart, timeout.Token);
                }
                return await FinishAsync(workspaceId, operation, timeout.Token);
            }
            if (snapshot.Connection.QrExpiresAt > _clock.UtcNow && snapshot.Connection.QrBase64 != null) return await FinishAsync(workspaceId, operation, timeout.Token);
            RequireWebhookConfiguration();
            if (!remote.Exists)
            {
                using var create = await _http.PostAsJsonAsync($"{_baseUrl}/instance/create", new
                {
                    instanceName = remote.Name, integration = "WHATSAPP-BAILEYS", qrcode = false
                }, timeout.Token);
                // An unknown create result is reconciled through fetchInstances on the next explicit attempt.
                if (create.StatusCode != HttpStatusCode.Conflict) await ReadSuccessAsync(create, timeout.Token);
                remote = await InspectAsync(workspaceId, remote.Name, timeout.Token);
                if (!remote.Exists) throw new HttpRequestException("No se pudo confirmar la creación de la instancia.");
                if (remote.HasSession)
                {
                    await _connections.SaveObservationAsync(workspaceId, operation, new(remote.State == "open" ? "CONNECTED" : "RECONNECTING", true), _clock.UtcNow, timeout.Token);
                    return await FinishAsync(workspaceId, operation, timeout.Token);
                }
            }
            await ConfigureWebhookAsync(remote.Name, timeout.Token);
            using var connect = await _http.GetAsync(Url("instance/connect", remote.Name), timeout.Token);
            var result = await ReadSuccessAsync(connect, timeout.Token);
            if (State(result) == "open")
                await _connections.SaveObservationAsync(workspaceId, operation, new("CONNECTED", true), _clock.UtcNow, timeout.Token);
            else
            {
                var qr = ExtractQr(result);
                if (string.IsNullOrWhiteSpace(qr)) throw new HttpRequestException("Evolution no devolvió un QR válido. Consulta el estado antes de reintentar.");
                await _connections.SaveObservationAsync(workspaceId, operation, new("QR_AVAILABLE", false, qr, _clock.UtcNow.AddSeconds(30)), _clock.UtcNow, timeout.Token);
            }
            return await FinishAsync(workspaceId, operation, timeout.Token);
        }
        finally { await ReleaseSafelyAsync(workspaceId, operation); }
    }

    public async Task<WhatsAppConnectionStatus> DisconnectAsync(Guid workspaceId, bool confirmed, CancellationToken ct)
    {
        if (!confirmed) throw new DomainException("Debes confirmar explícitamente la desconexión para cambiar de número.");
        var before = await _connections.GetAsync(workspaceId, ct);
        if (before.Connection.LastLogoutAt != null && before.Connection.IsLinked == false && before.Connection.QrBase64 == null && !before.Connection.LogoutPending)
            return Present(before.Connection);
        var operation = await _connections.AcquireAsync(workspaceId, true, _clock.UtcNow, _clock.UtcNow.AddMinutes(2), ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(90));
        try
        {
            var snapshot = await _connections.GetAsync(workspaceId, timeout.Token);
            var remote = await InspectAsync(workspaceId, snapshot.InstanceName, timeout.Token);
            var owner = remote.Owner;
            // A temporarily absent instance is not proof of logout: always request an explicit acknowledgement.
            using var logout = await _http.DeleteAsync(Url("instance/logout", remote.Name), timeout.Token);
            if (logout.StatusCode == HttpStatusCode.BadRequest)
            {
                var proof = await InspectAsync(workspaceId, remote.Name, timeout.Token);
                if (!proof.Exists || !(proof.State is "close" or "closed" && proof.LoggedOut))
                    throw new HttpRequestException("Evolution no confirmó el cierre de la sesión. La vinculación se conserva.");
            }
            else
            {
                var acknowledgement = await ReadSuccessAsync(logout, timeout.Token);
                if (!string.Equals(String(acknowledgement, "status"), "SUCCESS", StringComparison.OrdinalIgnoreCase))
                    throw new HttpRequestException("Evolution no confirmó la desconexión. La vinculación se conserva.");
            }
            remote = await InspectAsync(workspaceId, remote.Name, timeout.Token);
            if (!remote.Exists || remote.State is not ("close" or "closed"))
                throw new HttpRequestException("Evolution todavía no confirmó el cierre de la sesión. La vinculación se conserva.");
            await _connections.ConfirmLogoutAsync(workspaceId, operation, owner, _clock.UtcNow, timeout.Token);
            return await FinishAsync(workspaceId, operation, timeout.Token);
        }
        finally { await ReleaseSafelyAsync(workspaceId, operation); }
    }

    public async Task ObserveConnectionAsync(Guid workspaceId, string state, CancellationToken ct)
    {
        var snapshot = await _connections.GetAsync(workspaceId, ct);
        if (snapshot.Connection.LogoutPending) return;
        // A delayed open event after confirmed logout cannot revive the previous session.
        if (state == "open" && snapshot.Connection.LastLogoutAt != null && snapshot.Connection.IsLinked == false
            && snapshot.Connection.QrBase64 == null && snapshot.Connection.OperationId == null) return;
        if (state == "open") await _connections.SaveObservationAsync(workspaceId, null, new("CONNECTED", true), _clock.UtcNow, ct, snapshot.Connection.PersistenceVersion);
        else if (state is "close" or "closed" or "connecting")
            await _connections.SaveObservationAsync(workspaceId, null, new(snapshot.Connection.IsLinked == true ? "RECONNECTING" : "CONNECTING", false,
                snapshot.Connection.QrBase64, snapshot.Connection.QrExpiresAt), _clock.UtcNow, ct, snapshot.Connection.PersistenceVersion);
        else throw new ArgumentException("Estado de conexión inválido.");
    }

    private async Task<RemoteInstance> InspectAsync(Guid workspaceId, string name, CancellationToken ct)
    {
        var remote = await FetchAsync(name, ct);
        if (remote.Exists) return remote;
        var alias = EvolutionInstanceIdentity.LegacyAlias(name);
        if (alias == name) return remote;
        // The repository rejects ambiguous legacy ownership before adopting the actual provider identity.
        var legacy = await FetchAsync(alias, ct);
        if (legacy.Exists) { await _connections.AdoptLegacyNameAsync(workspaceId, name, alias, ct); return legacy; }
        return remote;
    }

    private async Task<RemoteInstance> FetchAsync(string name, CancellationToken ct)
    {
        using var response = await _http.GetAsync($"{_baseUrl}/instance/fetchInstances?instanceName={Uri.EscapeDataString(name)}", ct);
        JsonElement json;
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Evolution 2.3.7 can reject the filtered lookup even when the general listing succeeds.
            using var listing = await _http.GetAsync($"{_baseUrl}/instance/fetchInstances", ct);
            json = await ReadSuccessAsync(listing, ct);
        }
        else json = await ReadSuccessAsync(response, ct);
        if (json.ValueKind != JsonValueKind.Array) throw new HttpRequestException("Evolution devolvió datos de instancia inválidos.");
        JsonElement? found = null;
        foreach (var item in json.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) throw new HttpRequestException("Evolution devolvió datos de instancia inválidos.");
            var instance = item.TryGetProperty("instance", out var nested) && nested.ValueKind == JsonValueKind.Object ? nested : item;
            foreach (var field in new[] { "name", "instanceName" })
                if (instance.TryGetProperty(field, out var identity) && identity.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                    throw new HttpRequestException("Evolution devolvió datos de instancia inválidos.");
            var currentName = String(instance, "name");
            var legacyName = String(instance, "instanceName");
            if (currentName != null && legacyName != null && currentName != legacyName)
                throw new HttpRequestException("Evolution devolvió una identidad ambigua.");
            var returnedName = currentName ?? legacyName;
            if (string.IsNullOrWhiteSpace(returnedName)) throw new HttpRequestException("Evolution devolvió datos de instancia inválidos.");
            if (returnedName != name) continue;
            if (found != null) throw new HttpRequestException("Evolution devolvió una identidad ambigua.");
            found = instance;
        }
        if (found is not { } match) return new(name, false, "close", false, null, false);
        var state = (String(match, "connectionStatus") ?? String(match, "state") ?? String(match, "status"))?.ToLowerInvariant();
        if (state is not ("open" or "close" or "closed" or "connecting" or "created"))
            throw new HttpRequestException("Evolution no confirmó el estado de la sesión.");
        // Missing owner metadata is not proof that a legacy session is unlinked.
        if (state != "open" && !match.TryGetProperty("ownerJid", out _) && !match.TryGetProperty("owner", out _))
            throw new HttpRequestException("Evolution no confirmó si existe una sesión. La vinculación se conserva.");
        var owner = String(match, "ownerJid") ?? String(match, "owner");
        foreach (var field in new[] { "ownerJid", "owner" })
            if (match.TryGetProperty(field, out var ownerValue) && ownerValue.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))
                throw new HttpRequestException("Evolution devolvió datos de sesión inválidos.");
        var loggedOut = match.TryGetProperty("disconnectionReasonCode", out var reason)
            && ((reason.ValueKind == JsonValueKind.Number && reason.TryGetInt32(out var code) && code == 401)
                || reason.ValueKind == JsonValueKind.String && reason.GetString() == "401");
        return new(name, true, state, state == "open" || !string.IsNullOrWhiteSpace(owner), owner, loggedOut);
    }

    private void RequireWebhookConfiguration()
    {
        if (!Uri.TryCreate(_webhookUrl, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || string.IsNullOrWhiteSpace(_webhookKey))
            throw new HttpRequestException("No está disponible la configuración segura del webhook.");
    }
    private async Task ConfigureWebhookAsync(string name, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(Url("webhook/set", name), new
        {
            webhook = new { enabled = true, url = _webhookUrl, byEvents = false, base64 = false,
                events = new[] { "MESSAGES_UPSERT", "CONNECTION_UPDATE" },
                headers = new Dictionary<string, string> { ["X-NexFlow-Webhook-Key"] = InstanceWebhookKey(_webhookKey, name) } }
        }, ct);
        await ReadSuccessAsync(response, ct);
    }
    public static string InstanceWebhookKey(string secret, string name) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(name))).ToLowerInvariant();
    private string Url(string path, string name) => $"{_baseUrl}/{path}/{Uri.EscapeDataString(name)}";
    private static string? String(JsonElement json, string key) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static string? State(JsonElement json) => json.ValueKind == JsonValueKind.Object && json.TryGetProperty("instance", out var instance) ? (String(instance, "state") ?? String(instance, "status"))?.ToLowerInvariant() : null;
    private static string? ExtractQr(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Object) throw new HttpRequestException("Evolution devolvió una respuesta de conexión inválida.");
        if (String(json, "base64") is { } qr) return qr;
        if (json.TryGetProperty("qrcode", out var nested)) return nested.ValueKind == JsonValueKind.String ? nested.GetString() : String(nested, "base64");
        return json.TryGetProperty("hash", out var hash) ? String(hash, "qrcode") : null;
    }
    private static async Task<JsonElement> ReadSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Evolution no pudo confirmar la operación.", null, response.StatusCode);
        JsonElement json;
        try { json = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct); }
        catch (JsonException) { throw new HttpRequestException("Evolution devolvió una respuesta inválida."); }
        if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.False && error.ValueKind != JsonValueKind.Null)
            throw new HttpRequestException("Evolution no pudo confirmar la operación.");
        return json;
    }
    private async Task ReleaseSafelyAsync(Guid workspaceId, Guid operation)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await _connections.ReleaseAsync(workspaceId, operation, timeout.Token); }
        catch (Exception) { _logger.LogWarning("WhatsApp operation lease will expire for workspace {WorkspaceId}", workspaceId); }
    }
    private async Task<WhatsAppConnectionStatus> FinishAsync(Guid workspaceId, Guid operation, CancellationToken ct)
    {
        await _connections.ReleaseAsync(workspaceId, operation, ct);
        return Present((await _connections.GetAsync(workspaceId, ct)).Connection);
    }
    private WhatsAppConnectionStatus Present(WhatsAppConnection row)
    {
        var linked = row.IsLinked == true;
        var expires = row.QrExpiresAt;
        var status = row.LogoutPending ? "DISCONNECT_PENDING" : !linked && row.OperationUntil > _clock.UtcNow && row.QrBase64 == null ? "CONNECTING"
            : row.Status == "QR_AVAILABLE" && expires <= _clock.UtcNow ? "QR_EXPIRED" : row.Status;
        var qr = !linked && status == "QR_AVAILABLE" && expires > _clock.UtcNow ? row.QrBase64 : null;
        return new(status, linked, !linked && !row.LogoutPending && (row.OperationUntil == null || row.OperationUntil <= _clock.UtcNow),
            linked || row.LogoutPending, qr, qr != null ? expires : null,
            row.LogoutPending ? "La desconexión sigue pendiente. Reintenta confirmarla; no se permite vincular otro número."
                : linked && status != "CONNECTED" ? "La sesión sigue vinculada. Puedes revisar su conexión o desconectarla explícitamente." : null);
    }
    private static bool HasSession(RemoteInstance remote, WhatsAppConnection row) => remote.State == "open"
        || remote.HasSession && !(row.LastLogoutAt != null && row.IsLinked == false && remote.Owner == row.LoggedOutOwner);
    private sealed record RemoteInstance(string Name, bool Exists, string State, bool HasSession, string? Owner, bool LoggedOut);
}
