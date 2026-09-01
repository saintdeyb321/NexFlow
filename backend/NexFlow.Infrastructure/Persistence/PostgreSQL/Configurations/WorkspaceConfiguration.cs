using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Name).IsRequired().HasMaxLength(150);
        builder.Property(w => w.Status).HasConversion<string>().IsRequired();
        builder.Property(w => w.EvolutionInstanceName).HasMaxLength(150);
        builder.HasIndex(w => w.EvolutionInstanceName).IsUnique();
    }
}