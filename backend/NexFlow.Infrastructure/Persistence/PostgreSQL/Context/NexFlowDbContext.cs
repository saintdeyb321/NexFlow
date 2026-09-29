using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

public class NexFlowDbContext : DbContext, IUnitOfWork
{
    private readonly IWorkspaceContext? _workspaceContext;

    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<LicenseModule> LicenseModules => Set<LicenseModule>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<ModuleCapability> ModuleCapabilities => Set<ModuleCapability>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateModule> TemplateModules => Set<TemplateModule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Reservation> Reservations { get; set; } = null!;
    public DbSet<SystemAdministrator> SystemAdministrators { get; set; } = null!;
    public DbSet<Notification> Notifications { get; set; } = null!;
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    // 🔥 SPRINT 01: Nueva Inbox Duradera reemplaza a ProcessedMessages
    public DbSet<InboundMessage> InboundMessages => Set<InboundMessage>();

    public Guid TenantId => _workspaceContext?.CurrentWorkspaceId ?? Guid.Empty;

    public NexFlowDbContext(DbContextOptions<NexFlowDbContext> options, IWorkspaceContext? workspaceContext = null) : base(options)
    {
        _workspaceContext = workspaceContext;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NexFlowDbContext).Assembly);

        modelBuilder.Entity<Reservation>().HasQueryFilter(e => TenantId == Guid.Empty || e.WorkspaceId == TenantId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(e => TenantId == Guid.Empty || e.WorkspaceId == TenantId);
        modelBuilder.Entity<Membership>().HasQueryFilter(e => TenantId == Guid.Empty || e.WorkspaceId == TenantId);
        modelBuilder.Entity<License>().HasQueryFilter(e => TenantId == Guid.Empty || e.WorkspaceId == TenantId);

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PayloadJson).HasColumnType("jsonb");
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<Reservation>()
            .HasIndex(r => new { r.WorkspaceId, r.LocationId, r.Status, r.StartTime, r.EndTime })
            .HasDatabaseName("IX_Reservations_TimeRangeOverlap");

        // 🔥 SPRINT 01: Configuración de la Inbox
        modelBuilder.Entity<InboundMessage>(entity =>
        {
            entity.HasKey(e => e.Id);
            // Evita que Evolution duplique el mismo mensaje en caso de reintentos suyos
            entity.HasIndex(e => new { e.InstanceName, e.ExternalMessageId }).IsUnique();
            entity.HasIndex(e => e.Status); // Agiliza el Worker
        });

        base.OnModelCreating(modelBuilder);
    }
}