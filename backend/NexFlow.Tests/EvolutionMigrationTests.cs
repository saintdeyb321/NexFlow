using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using System.Data;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;
using Xunit;

namespace NexFlow.Tests;

public sealed class EvolutionMigrationTests
{
    [Fact]
    public void Generated_snapshot_matches_model_and_migration_only_changes_whatsapp_storage_and_missing_names()
    {
        using var db = new NexFlowDbContext(new DbContextOptionsBuilder<NexFlowDbContext>()
            .UseNpgsql("Host=127.0.0.1;Database=unit_test;Username=unit_test;Password=unit_test").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var workspace = db.Model.FindEntityType(typeof(Workspace))!;
        Assert.True(Assert.Single(workspace.GetIndexes(), i => i.Properties.SingleOrDefault()?.Name == nameof(Workspace.EvolutionInstanceName)).IsUnique);
        var connection = db.Model.FindEntityType(typeof(WhatsAppConnection))!;
        Assert.Equal(nameof(WhatsAppConnection.WorkspaceId), Assert.Single(connection.FindPrimaryKey()!.Properties).Name);
        Assert.True(Assert.Single(connection.GetForeignKeys()).IsUnique);
        foreach (var field in new[] { "IsLinked", "QrBase64", "QrExpiresAt", "ObservedAt", "OperationId", "OperationUntil", "LastLogoutAt", "LoggedOutOwner" })
            Assert.True(connection.FindProperty(field)!.IsNullable);
        foreach (var field in new[] { "WorkspaceId", "PersistenceVersion", "Status", "LogoutPending" })
            Assert.False(connection.FindProperty(field)!.IsNullable);
        var migrations = db.GetService<IMigrationsAssembly>().Migrations.Keys.ToArray();
        Assert.EndsWith("_TrackWorkspaceWhatsAppConnections", migrations[^1]);
        var script = db.GetService<IMigrator>().GenerateScript(migrations[^2], migrations[^1]);
        Assert.Contains("CREATE TABLE \"WhatsAppConnections\"", script);
        Assert.Contains("UPDATE \"Workspaces\"", script);
        Assert.Contains("IS NULL OR btrim(\"EvolutionInstanceName\") = ''", script);
        Assert.DoesNotContain("ALTER TABLE", script);
        Assert.DoesNotContain("DELETE FROM", script);
        var down = db.GetService<IMigrator>().GenerateScript(migrations[^1], migrations[^2]);
        Assert.Contains("DROP TABLE \"WhatsAppConnections\"", down);
        Assert.DoesNotContain("UPDATE \"Workspaces\"", down);
        Assert.Equal(ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
