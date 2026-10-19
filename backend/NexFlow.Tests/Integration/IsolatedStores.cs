using System.Diagnostics;
using Google.Api.Gax;
using Google.Cloud.Firestore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Reservation;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.LocationAvailability;
using NexFlow.Application.Features.Reservations;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.Firestore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using Npgsql;
using Xunit;
using IClock = NexFlow.Application.Abstractions.IClock;

namespace NexFlow.Tests.Integration;

// Deliberately separate from the 669 unit tests. Opt-in never silently passes a
// missing dependency: once enabled, an unavailable store fails fixture creation.
public sealed class IsolatedIntegrationFactAttribute : FactAttribute
{
    public IsolatedIntegrationFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("NEXFLOW_RC3_INTEGRATION") != "1")
            Skip = "Integration NOT EXECUTED: requires disposable PostgreSQL + Firestore Emulator (RC3 manual workflow).";
    }
}

internal static class IntegrationSafety
{
    public static NpgsqlConnectionStringBuilder Postgres(string value)
    {
        var b = new NpgsqlConnectionStringBuilder(value);
        if (b.Host is not ("127.0.0.1" or "localhost" or "::1") || b.Database != "nexflow_rc3_admin"
            || b.Username != "nexflow_rc3" || b.Password != "local-test-only"
            || !string.IsNullOrEmpty(b.Passfile) || !string.IsNullOrEmpty(b.SslCertificate))
            throw new InvalidOperationException("Only the disposable loopback RC3 database and synthetic credentials are allowed.");
        b.SslMode = SslMode.Disable; b.IncludeErrorDetail = false;
        b.Timeout = 5; b.CommandTimeout = 15; b.MaxPoolSize = 8;
        b.ApplicationName = "nexflow-rc3-integration";
        return b;
    }

    public static void Emulator(string? host, string? project)
    {
        if (!Uri.TryCreate("http://" + host, UriKind.Absolute, out var endpoint)
            || endpoint.Host is not ("127.0.0.1" or "localhost" or "[::1]")
            || endpoint.Port <= 0 || project != "demo-nexflow-rc3")
            throw new InvalidOperationException("Only the loopback Firestore Emulator and demo project are allowed.");
    }
}

internal sealed class IsolatedStores : IAsyncDisposable
{
    internal const string Previous = "20261008050017_TrackWorkspaceWhatsAppConnections";
    internal const string Target = "20261019170831_CoordinateReservationResourcesAndReuseCancelledSlots";
    internal static readonly DateTime Day = new(2030, 7, 2, 0, 0, 0, DateTimeKind.Utc);
    private readonly NpgsqlConnectionStringBuilder admin;
    private readonly string database = "nexflow_rc3_" + Guid.NewGuid().ToString("N");
    private readonly HashSet<Guid> workspaces = [];
    private readonly CancellationTokenSource deadline = new(TimeSpan.FromSeconds(90));
    public CancellationToken Ct => deadline.Token;
    public FirestoreDb Firestore { get; }
    public IClock Clock { get; } = new TestClock();
    public string ConnectionString { get; }

    private IsolatedStores()
    {
        admin = IntegrationSafety.Postgres(Environment.GetEnvironmentVariable("NEXFLOW_RC3_POSTGRES") ?? "");
        IntegrationSafety.Emulator(Environment.GetEnvironmentVariable("FIRESTORE_EMULATOR_HOST"),
            Environment.GetEnvironmentVariable("GCLOUD_PROJECT"));
        Firestore = new FirestoreDbBuilder { ProjectId = "demo-nexflow-rc3", EmulatorDetection = EmulatorDetection.EmulatorOnly }.Build();
        ConnectionString = new NpgsqlConnectionStringBuilder(admin.ConnectionString) { Database = database }.ConnectionString;
    }

    public static async Task<IsolatedStores> Create(string migration = Target)
    {
        var f = new IsolatedStores();
        try
        {
            await using var connection = new NpgsqlConnection(f.admin.ConnectionString);
            await connection.OpenAsync(f.Ct);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{f.database}\"", connection);
            await command.ExecuteNonQueryAsync(f.Ct);
            await using var db = f.Db();
            await db.GetService<IMigrator>().MigrateAsync(migration, f.Ct);
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }

    public NexFlowDbContext Db(params IInterceptor[] interceptors) => new(new DbContextOptionsBuilder<NexFlowDbContext>()
        .UseNpgsql(ConnectionString).AddInterceptors(interceptors).Options);

    public ReservationResourceCoordinator Coordinator(NexFlowDbContext db) => new(db, Clock);
    public ReservationResourceKey Key(Guid workspace, bool location = false) => new(workspace,
        location ? ReservationResourceKind.Location : ReservationResourceKind.Service, location ? "location" : "service");
    public DocumentReference Root(Guid workspace) => Firestore.Collection("workspaces").Document(workspace.ToString());
    public static ServiceDto Service(string name = "Service") => new() { Id = "service", Name = name, Currency = "PEN",
        IsActive = true, RequiresReservation = true, DurationInMinutes = 30, LocationScope = "ALL" };

    public async Task<Guid> Workspace(bool seedFirestore = true)
    {
        await using var db = Db(); var workspace = NexFlow.Domain.Entities.Workspace.Create("RC3 synthetic");
        db.Workspaces.Add(workspace); await db.SaveChangesAsync(Ct); workspaces.Add(workspace.Id);
        if (!seedFirestore) return workspace.Id;
        var resources = Coordinator(db);
        var locations = new FirestoreLocationRepository(Firestore, resources);
        Assert.True((await locations.SaveLocationAsync(workspace.Id, new("location", "Main", "Synthetic", null, null, true), true, 3, Ct)).IsSuccess);
        Assert.True((await locations.SaveLocationAsync(workspace.Id, new("second", "Second", "Synthetic", null, null, false), true, 3, Ct)).IsSuccess);
        await new FirestoreCatalogRepository(Firestore, resources).SaveItemAsync(workspace.Id, Service(), true, Ct);
        await new FirestoreBusinessProfileRepository(Firestore).SaveProfileAsync(workspace.Id,
            new("RC3", "", "", "", "", "America/Lima"), Ct);
        await new FirestoreBusinessHoursRepository(Firestore).SaveBusinessHoursAsync(workspace.Id, "location",
            [new BusinessHoursDto(2, "09:00", "18:00", false)], Ct);
        return workspace.Id;
    }

    public ReservationEngine Engine(NexFlowDbContext db, IReservationResourceCoordinator? resources = null)
    {
        resources ??= Coordinator(db);
        var catalog = new FirestoreCatalogRepository(Firestore, resources);
        var locations = new FirestoreLocationRepository(Firestore, resources);
        return new(new ReservationRepository(db, Clock), catalog, new FirestoreBusinessHoursRepository(Firestore),
            new FirestoreBusinessProfileRepository(Firestore), locations, db,
            new LocationAvailabilityService(catalog, locations), Clock, new PostgresOutboxRepository(db), resources);
    }

    public Task<NexFlow.Application.Common.Result<ReservationDto>> Book(NexFlowDbContext db, Guid ws, string source,
        IReservationResourceCoordinator? resources = null, int minute = 0) => Engine(db, resources)
        .CreateReservationAsync(ws, "location", "service", "synthetic", "Synthetic", Day.AddHours(15).AddMinutes(minute), Ct, source);

    public async Task WaitForBlocked(int pid)
    {
        // Poll an observable PostgreSQL condition, never infer a lock from elapsed sleep.
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct); timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await using var observer = new NpgsqlConnection(ConnectionString); await observer.OpenAsync(timeout.Token);
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT cardinality(pg_blocking_pids(@pid)) > 0", observer);
            command.Parameters.AddWithValue("pid", pid);
            if (await command.ExecuteScalarAsync(timeout.Token) is true) return;
            await Task.Delay(20, timeout.Token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Only generated UUID databases and registered synthetic workspace roots.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            foreach (var ws in workspaces)
                await new FirestoreTenantCleanupService(Firestore, Microsoft.Extensions.Logging.Abstractions.NullLogger<FirestoreTenantCleanupService>.Instance)
                    .PurgeTenantDataAsync(ws, cleanup.Token);
        }
        finally
        {
            using var poolKey = new NpgsqlConnection(ConnectionString);
            NpgsqlConnection.ClearPool(poolKey);
            await using var connection = new NpgsqlConnection(admin.ConnectionString); await connection.OpenAsync(cleanup.Token);
            await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync(cleanup.Token);
            deadline.Dispose();
        }
    }
    private sealed class TestClock : IClock { public DateTime UtcNow => Day.AddDays(-1); }
}

internal sealed class AsyncGate : IDisposable
{
    private readonly TaskCompletionSource reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task Reached => reached.Task.WaitAsync(TimeSpan.FromSeconds(8));
    public async Task Stop(CancellationToken ct) { reached.TrySetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(12), ct); }
    public void Open() => release.TrySetResult();
    public void Dispose() => Open();
}

// Faults/barriers at boundaries; every SQL command still uses production Npgsql.
internal sealed class CoordinatorProbe(IReservationResourceCoordinator inner) : IReservationResourceCoordinator
{
    public Func<CancellationToken, Task>? BeforeValidate { get; set; }
    public Func<ReservationResourceMutation, Task>? BeforeComplete { get; set; }
    public Task<ReservationResourceStamp> ReadAsync(ReservationResourceKey key, CancellationToken ct) => inner.ReadAsync(key, ct);
    public Task<ReservationResourceMutation> BeginAsync(ReservationResourceStamp stamp, ReservationResourceChange change, CancellationToken ct) => inner.BeginAsync(stamp, change, ct);
    public async Task ValidateAsync(IReadOnlyList<ReservationResourceStamp> stamps, CancellationToken ct)
    { if (BeforeValidate != null) await BeforeValidate(ct); await inner.ValidateAsync(stamps, ct); }
    public async Task CompleteAsync(ReservationResourceMutation op, CancellationToken ct)
    { if (BeforeComplete != null) await BeforeComplete(op); await inner.CompleteAsync(op, ct); }
    public Task<IReadOnlyList<ReservationResourceMutation>> ClaimPendingAsync(CancellationToken ct) => inner.ClaimPendingAsync(ct);
    public Task ExecuteRecoveryAsync(Guid workspace, Func<Task> apply, CancellationToken ct) => inner.ExecuteRecoveryAsync(workspace, apply, ct);
}
