using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.HasKey(m => m.Id);

        // El código del módulo (ej. "FAQ") debe ser único
        builder.HasIndex(m => m.Code).IsUnique();

        builder.Property(m => m.Status).HasConversion<string>().IsRequired();
    }
}