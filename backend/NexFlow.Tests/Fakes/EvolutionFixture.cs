using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using NexFlow.Infrastructure.Gateways;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NexFlow.Tests.Fakes;

public sealed class EvolutionFixture : IAsyncDisposable
{
    public sealed class Clock : IClock { public DateTime UtcNow { get; set; } = DateTime.UtcNow; }
    public Clock Time { get; } = new();
    public SqliteConnection Connection { get; } = new("Data Source=:memory:");
    public EvolutionDb Db { get; private set; } = null!;
    public WhatsAppConnectionRepository Repository { get; private set; } = null!;
    public Workspace Workspace { get; } = Workspace.Create("Example Business");
    public Provider Transport { get; } = new();
    public HttpClient Client { get; private set; } = null!;
    public EvolutionConnectionService Service { get; private set; } = null!;
    public IConfiguration Config { get; } = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Evolution:BaseUrl"] = "https://evolution.example.test", ["Evolution:ApiKey"] = "unit-test-key",
        ["Evolution:WebhookUrl"] = "https://backend.example.test/api/webhooks/evolution", ["Evolution:WebhookKey"] = "unit-test-webhook"
    }).Build();

    public static async Task<EvolutionFixture> CreateAsync()
    {
        var fixture = new EvolutionFixture();
        await fixture.Connection.OpenAsync();
        fixture.Db = fixture.NewContext();
        await fixture.Db.Database.EnsureCreatedAsync();
        fixture.Db.Workspaces.Add(fixture.Workspace);
        await fixture.Db.SaveChangesAsync();
        fixture.Repository = new(fixture.Db);
        fixture.Client = new(fixture.Transport);
        fixture.Transport.Name = fixture.Workspace.EvolutionInstanceName!;
        fixture.Transport.BeforeRequest = () => Xunit.Assert.Null(fixture.Db.Database.CurrentTransaction);
        fixture.Service = new(fixture.Client, fixture.Config, fixture.Repository, fixture.Time, NullLogger<EvolutionConnectionService>.Instance);
        return fixture;
    }
    public EvolutionDb NewContext(params Microsoft.EntityFrameworkCore.Diagnostics.IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<NexFlowDbContext>().UseSqlite(Connection).AddInterceptors(interceptors).Options);
    public async Task<WhatsAppConnection> State() => (await Repository.GetAsync(Workspace.Id, default)).Connection;
    public async ValueTask DisposeAsync() { Client.Dispose(); await Db.DisposeAsync(); await Connection.DisposeAsync(); }

    public sealed class EvolutionDb(DbContextOptions<NexFlowDbContext> options) : NexFlowDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes().ToArray()) modelBuilder.Ignore(entity.ClrType);
            new WorkspaceConfiguration().Configure(modelBuilder.Entity<Workspace>());
            modelBuilder.Entity<Workspace>().Ignore(w => w.DomainEvents);
            new WhatsAppConnectionConfiguration().Configure(modelBuilder.Entity<WhatsAppConnection>());
        }
    }

    public sealed class Provider : HttpMessageHandler
    {
        public string Name { get; set; } = string.Empty;
        public bool Exists { get; set; }
        public string State { get; set; } = "close";
        public string? Owner { get; set; }
        public bool IncludeOwner { get; set; } = true;
        public bool LegacyFormat { get; set; }
        public bool LoseCreateAcknowledgement { get; set; }
        public bool FailLogout { get; set; }
        public bool FailRequests { get; set; }
        public bool RejectClosedLogout { get; set; }
        public bool RejectMissingLogout { get; set; }
        public bool DisappearAfterLogout { get; set; }
        public string LogoutAcknowledgement { get; set; } = "SUCCESS";
        public string StateAfterLogout { get; set; } = "close";
        public HttpStatusCode MissingLogoutStatus { get; set; } = HttpStatusCode.NotFound;
        public int? DisconnectionReasonCode { get; set; }
        public TaskCompletionSource? FirstFetchEntered { get; set; }
        public TaskCompletionSource? ContinueFirstFetch { get; set; }
        public Action? BeforeRequest { get; set; }
        public List<(string Method, string Path, string Body)> Calls { get; } = new();
        private int _qr;
        private bool _paused;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            BeforeRequest?.Invoke();
            var path = request.RequestUri!.AbsolutePath;
            var body = request.Content != null ? await request.Content.ReadAsStringAsync(ct) : string.Empty;
            lock (Calls) Calls.Add((request.Method.Method, path, body));
            if (FailRequests) throw new HttpRequestException("Simulated provider outage");
            object response;
            if (path == "/instance/fetchInstances")
            {
                if (!_paused && FirstFetchEntered != null)
                { _paused = true; FirstFetchEntered.SetResult(); await ContinueFirstFetch!.Task.WaitAsync(ct); }
                var requested = Uri.UnescapeDataString(request.RequestUri.Query["?instanceName=".Length..]);
                var metadata = new Dictionary<string, object?> { [LegacyFormat ? "instanceName" : "name"] = Name, [LegacyFormat ? "status" : "connectionStatus"] = State };
                if (IncludeOwner) metadata[LegacyFormat ? "owner" : "ownerJid"] = Owner;
                if (DisconnectionReasonCode is { } reason) metadata["disconnectionReasonCode"] = reason;
                response = Exists && requested == Name ? new object[] { LegacyFormat ? new { instance = metadata } : metadata } : Array.Empty<object>();
            }
            else if (path == "/instance/create")
            {
                using var payload = JsonDocument.Parse(body);
                Xunit.Assert.Equal(Name, payload.RootElement.GetProperty("instanceName").GetString());
                Xunit.Assert.False(payload.RootElement.GetProperty("qrcode").GetBoolean());
                Exists = true;
                response = new { instance = new { instanceName = Name, status = State } };
                if (LoseCreateAcknowledgement) { LoseCreateAcknowledgement = false; throw new HttpRequestException("Create succeeded, acknowledgement lost"); }
            }
            else if (path == $"/webhook/set/{Name}") response = new { enabled = true };
            else if (path == $"/instance/connect/{Name}") response = new { base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes($"qr-{++_qr}")) };
            else if (path == $"/instance/restart/{Name}") { State = "connecting"; response = new { instance = new { state = State } }; }
            else if (path == $"/instance/logout/{Name}")
            {
                if (RejectMissingLogout && !Exists) return new(MissingLogoutStatus) { Content = new StringContent("{\"error\":true}", Encoding.UTF8, "application/json") };
                if (RejectClosedLogout && State == "close") return new(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":true}", Encoding.UTF8, "application/json") };
                if (FailLogout) throw new HttpRequestException("Logout acknowledgement unavailable");
                State = StateAfterLogout; // Evolution retains historical owner metadata after logout.
                if (DisappearAfterLogout) Exists = false;
                response = new { status = LogoutAcknowledgement, error = false };
            }
            else throw new InvalidOperationException($"Unexpected provider call: {request.Method} {path}");
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response), Encoding.UTF8, "application/json") };
        }
    }
}
