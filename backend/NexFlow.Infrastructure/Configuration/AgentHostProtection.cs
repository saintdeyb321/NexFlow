using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using NexFlow.Application.Features.Automation.Agent;

namespace NexFlow.Infrastructure.Configuration;

public static class AgentHostProtection
{
    private sealed record Manifest(int Version, Guid DeploymentId, Guid WorkspaceId);
    private static InvalidOperationException Denied(string reason) => new("DEMO identity preflight: " + reason + ". Workers must not start.");

    public static AgentProtectionIdentity Load(IConfiguration configuration)
    {
        try {
            var role = configuration["NexFlowHost:Role"] ?? "Legacy";
            var path = configuration["AgentProtection:ManifestPath"];
            var pin = configuration["AgentProtection:ManifestSha256"];
            var agent = configuration.GetSection("AgentDemo").Get<AgentDemoOptions>() ?? new();
            if (role == "Legacy") {
                if (!string.IsNullOrWhiteSpace(path) || !string.IsNullOrWhiteSpace(pin) || agent.Enabled || agent.WorkspaceId != Guid.Empty
                    || !string.IsNullOrWhiteSpace(agent.InstanceName) || !string.IsNullOrWhiteSpace(agent.OperatorAuthorization) || agent.TestPhones is { Length: > 0 })
                    throw Denied("explicit Demo host role required");
                return AgentProtectionIdentity.Legacy;
            }
            if (role != "Demo") throw Denied("unknown host role");
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path) || path.StartsWith("\\\\", StringComparison.Ordinal)
                || pin is not { Length: 64 } || !pin.All(char.IsAsciiHexDigit)) throw Denied("absolute local manifest and SHA256 pin required");
            // Bounded, read-only metadata. Never create a replacement identity.
            using var stream = File.OpenRead(path);
            var bytes = new byte[4097]; var count = 0;
            while (count < bytes.Length) { var read = stream.Read(bytes.AsSpan(count)); if (read == 0) break; count += read; }
            if (count is 0 or > 4096) throw Denied("invalid manifest size");
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(bytes.AsSpan(0, count)), Convert.FromHexString(pin)))
                throw Denied("manifest attestation mismatch");
            var manifest = JsonSerializer.Deserialize<Manifest>(bytes.AsSpan(0, count), new JsonSerializerOptions {
                MaxDepth = 4, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow });
            if (manifest is not { Version: 1 } || manifest.WorkspaceId == Guid.Empty || manifest.DeploymentId == Guid.Empty)
                throw Denied("invalid manifest identity");
            if (agent.WorkspaceId != Guid.Empty && agent.WorkspaceId != manifest.WorkspaceId || agent.Enabled && agent.WorkspaceId == Guid.Empty)
                throw Denied("dynamic workspace disagrees with protected identity");
            return new(manifest.WorkspaceId, manifest.DeploymentId, pin.ToUpperInvariant());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException) {
            // Do not log payload, path, keys or parser fragments.
            throw Denied("manifest unavailable or malformed");
        }
        catch (InvalidOperationException ex) when (!ex.Message.StartsWith("DEMO identity preflight:", StringComparison.Ordinal)) {
            throw Denied("invalid deployment configuration");
        }
    }
}
