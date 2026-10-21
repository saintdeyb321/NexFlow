using Xunit;

namespace NexFlow.Tests.Integration;

public sealed class IntegrationSafetyTests
{
    [Theory]
    [InlineData("production.example:6379")] [InlineData("127.0.0.1:6379")]
    [InlineData("127.0.0.1:16379,password=secret")] [InlineData("")]
    public void Redis_certification_rejects_application_endpoints_and_credentials(string endpoint)=>
        Assert.Throws<InvalidOperationException>(()=>CommerceRedisIntegrationTests.Options(endpoint));
    [Fact]
    public void Accepts_only_explicit_synthetic_local_configuration()
    {
        var b = IntegrationSafety.Postgres("Host=127.0.0.1;Database=nexflow_rc3_admin;Username=nexflow_rc3;Password=local-test-only");
        Assert.False(b.IncludeErrorDetail); Assert.Equal(8, b.MaxPoolSize);
        IntegrationSafety.Emulator("127.0.0.1:8080", "demo-nexflow-rc3");
    }

    [Theory]
    [InlineData("production.example", "nexflow_rc3_admin", "nexflow_rc3", "local-test-only")]
    [InlineData("127.0.0.1", "production", "nexflow_rc3", "local-test-only")]
    [InlineData("127.0.0.1", "nexflow_rc3_admin", "postgres", "local-test-only")]
    [InlineData("127.0.0.1", "nexflow_rc3_admin", "nexflow_rc3", "real-secret")]
    public void Rejects_non_disposable_database(string host, string database, string user, string password) =>
        Assert.Throws<InvalidOperationException>(() => IntegrationSafety.Postgres($"Host={host};Database={database};Username={user};Password={password}"));

    [Theory]
    [InlineData("firestore.googleapis.com:443", "demo-nexflow-rc3")]
    [InlineData("127.0.0.1:8080", "real-project")]
    [InlineData(null, null)]
    public void Rejects_cloud_or_missing_emulator(string? host, string? project) =>
        Assert.Throws<InvalidOperationException>(() => IntegrationSafety.Emulator(host, project));
}
