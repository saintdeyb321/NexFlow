using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Npgsql;
using StackExchange.Redis;

namespace NexFlow.API.Configuration;

internal static class RuntimeConfiguration
{
    internal static string[] GetAllowedOrigins(IConfiguration configuration, bool production)
    {
        const string key = "Cors:AllowedOrigins";
        var values = ReadList(configuration, key);
        if (values.Length == 0 && !production) return ["http://localhost:5173"];
        if (values.Length == 0) throw Invalid(key);
        var origins = new List<string>();
        foreach (var value in values)
        {
            if (value.Contains('*') || !TryHttpUrl(value, out var uri)
                || uri!.AbsolutePath != "/") throw Invalid(key);
            origins.Add(uri.GetLeftPart(UriPartial.Authority));
        }
        if (production && origins.All(origin => IsLocalOrigin(new Uri(origin)))) throw Invalid(key);
        return origins.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    internal static IPNetwork[] GetKnownNetworks(IConfiguration configuration, bool production)
    {
        const string key = "ReverseProxy:KnownNetworks";
        var values = ReadList(configuration, key);
        if (values.Length == 0 && production) throw Invalid(key);
        var networks = new List<IPNetwork>();
        foreach (var value in values)
        {
            // Deployment CIDRs are supplied at runtime. Public, unspecified,
            // multicast and catch-all networks can never be trusted proxies.
            if (!IPNetwork.TryParse(value, out var network) || !IsPrivateOrLoopback(network)) throw Invalid(key);
            networks.Add(network);
        }
        return networks.Distinct().ToArray();
    }

    internal static void ValidateProduction(IConfiguration configuration)
    {
        const string postgresKey = "ConnectionStrings:DefaultConnection";
        var postgres = Required(configuration, postgresKey);
        try
        {
            var options = new NpgsqlConnectionStringBuilder(postgres);
            if (string.IsNullOrWhiteSpace(options.Host) || string.IsNullOrWhiteSpace(options.Database)
                || string.IsNullOrWhiteSpace(options.Username)) throw Invalid(postgresKey);
        }
        catch (ArgumentException) { throw Invalid(postgresKey); }

        const string redisKey = "ConnectionStrings:Redis";
        var redis = Required(configuration, redisKey);
        try
        {
            var options = ConfigurationOptions.Parse(redis);
            if (options.EndPoints.Count == 0 || options.EndPoints.Any(endpoint => endpoint switch
                {
                    DnsEndPoint dns => string.IsNullOrWhiteSpace(dns.Host) || dns.Port is < 1 or > 65535,
                    IPEndPoint ip => ip.Port is < 1 or > 65535,
                    _ => true
                })) throw Invalid(redisKey);
        }
        catch (ArgumentException) { throw Invalid(redisKey); }
        catch (FormatException) { throw Invalid(redisKey); }

        var projectId = Required(configuration, "Firebase:ProjectId");
        if (!Regex.IsMatch(projectId, "^[a-z][a-z0-9-]{4,28}[a-z0-9]$")) throw Invalid("Firebase:ProjectId");

        const string credentialsKey = "GOOGLE_APPLICATION_CREDENTIALS";
        var credentialsPath = Environment.GetEnvironmentVariable(credentialsKey);
        if (string.IsNullOrWhiteSpace(credentialsPath) || !Path.IsPathFullyQualified(credentialsPath)
            || !File.Exists(credentialsPath)) throw Invalid(credentialsKey);

        foreach (var key in new[] { "Gemini:ApiKey", "Groq:ApiKey", "Evolution:ApiKey", "Evolution:WebhookKey",
                     "N8n:WebhookSecret", "Cloudinary:ApiKey", "Cloudinary:ApiSecret" })
            Required(configuration, key);
        foreach (var key in new[] { "Gemini:Model", "Groq:Model", "Cloudinary:CloudName" })
            if (Required(configuration, key).Any(char.IsWhiteSpace)) throw Invalid(key);
        foreach (var key in new[] { "Evolution:BaseUrl", "Evolution:WebhookUrl", "N8n:BaseUrl" })
            if (!TryHttpUrl(Required(configuration, key), out _)) throw Invalid(key);
        foreach (var key in new[] { "Evolution:TimeoutSeconds", "N8n:TimeoutSeconds" })
            if (!int.TryParse(Required(configuration, key), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                || seconds is < 1 or > 120) throw Invalid(key);

        if (!Regex.IsMatch(Required(configuration, "N8n:EventsWebhookId"), "^[a-zA-Z0-9][a-zA-Z0-9_-]{0,127}$"))
            throw Invalid("N8n:EventsWebhookId");
        // The hosting shortcut accepts every proxy; use our explicit CIDRs.
        if (string.Equals(configuration["ASPNETCORE_FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(configuration["FORWARDEDHEADERS_ENABLED"], "true", StringComparison.OrdinalIgnoreCase))
            throw Invalid("ASPNETCORE_FORWARDEDHEADERS_ENABLED");

        GetAllowedOrigins(configuration, production: true);
        GetKnownNetworks(configuration, production: true);
    }

    private static string Required(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl)) throw Invalid(key);
        return value;
    }

    private static string[] ReadList(IConfiguration configuration, string key)
        => new[] { configuration[key] }.Concat(configuration.GetSection(key).GetChildren().Select(child => child.Value))
            .Where(value => value != null)
            .SelectMany(value => value!.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static bool TryHttpUrl(string value, [NotNullWhen(true)] out Uri? uri)
        => Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme is "http" or "https"
            && !string.IsNullOrWhiteSpace(uri.Host) && !uri.Host.Contains('*')
            && string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static bool IsLocalOrigin(Uri uri) => uri.IsLoopback || uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(uri.Host, out var address) && (address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)));

    private static bool IsPrivateOrLoopback(IPNetwork network)
    {
        var address = network.BaseAddress;
        var prefix = network.PrefixLength;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
            return (bytes[0] == 10 && prefix >= 8)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31 && prefix >= 12)
                || (bytes[0] == 192 && bytes[1] == 168 && prefix >= 16)
                || (bytes[0] == 127 && prefix >= 8);
        return address.AddressFamily == AddressFamily.InterNetworkV6
            && (((bytes[0] & 0xfe) == 0xfc && prefix >= 7) || (IPAddress.IsLoopback(address) && prefix == 128));
    }

    private static InvalidOperationException Invalid(string key) => new($"Missing or invalid configuration: {key}.");
}
