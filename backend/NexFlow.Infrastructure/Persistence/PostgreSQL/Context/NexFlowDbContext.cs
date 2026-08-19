using Microsoft.EntityFrameworkCore;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

public class NexFlowDbContext : DbContext, IUnitOfWork
{
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

    public NexFlowDbContext(DbContextOptions<NexFlowDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Esto le dice a EF Core que busque todas las configuraciones (IEntityTypeConfiguration) 
        // en este mismo proyecto y las aplique automáticamente.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NexFlowDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}