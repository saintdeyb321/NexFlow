using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class ModuleCapabilityConfiguration : IEntityTypeConfiguration<ModuleCapability>
{
    public void Configure(EntityTypeBuilder<ModuleCapability> builder)
    {
        builder.HasKey(mc => mc.Id);

        builder.Property(mc => mc.Code).IsRequired().HasMaxLength(50);
        builder.Property(mc => mc.Description).IsRequired().HasMaxLength(200);

        // Índice compuesto: Un módulo no puede tener la misma capacidad repetida
        builder.HasIndex(mc => new { mc.ModuleId, mc.Code }).IsUnique();
    }
}