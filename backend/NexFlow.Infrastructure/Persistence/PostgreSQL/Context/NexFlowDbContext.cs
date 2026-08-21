using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;
using System;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

public class NexFlowDbContext : DbContext, IUnitOfWork
{
    // Inyectamos el contexto de la petición HTTP
    private readonly IWorkspaceContext? _workspaceContext;

    public DbSet<User> Users => Set<User>();
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<Membership> Memberships => Set<Membership>();
    public DbSet<License> Licenses => Set<License>();
    public DbSet<LicenseModule> LicenseModules => Set<LicenseModule>();
    public DbSet<Module> Modules => Set<Module>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<TemplateModule> TemplateModules => Set<TemplateModule>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Reservation> Reservations { get; set; } = null!;
    public DbSet<SystemAdministrator> SystemAdministrators { get; set; } = null!;

    // Propiedad que EF Core evaluará dinámicamente en CADA consulta
    public Guid TenantId => _workspaceContext?.CurrentWorkspaceId ?? Guid.Empty;

    // Permitimos que IWorkspaceContext sea null para que las migraciones de diseño no exploten
    public NexFlowDbContext(DbContextOptions<NexFlowDbContext> options, IWorkspaceContext? workspaceContext = null) : base(options)
    {
        _workspaceContext = workspaceContext;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NexFlowDbContext).Assembly);
        // 🛡️ AISLAMIENTO MULTI-TENANT (GLOBAL QUERY FILTERS)
        modelBuilder.Entity<Reservation>().HasQueryFilter(e => TenantId == Guid.Empty || e.WorkspaceId == TenantId);
        modelBuilder.Entity<AuditLog>().HasQueryFilter(e => TenantId == Guid.Empty || e.WorkspaceId == TenantId);
        modelBuilder.Entity<Membership>().HasQueryFilter(e => TenantId == Guid.Empty || e.WorkspaceId == TenantId);
        modelBuilder.Entity<License>().HasQueryFilter(e => TenantId == Guid.Empty || e.WorkspaceId == TenantId);

        base.OnModelCreating(modelBuilder);
    }
}