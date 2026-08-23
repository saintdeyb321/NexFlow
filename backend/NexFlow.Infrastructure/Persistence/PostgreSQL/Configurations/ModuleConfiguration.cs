using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class ModuleConfiguration : IEntityTypeConfiguration<Module>
{
    public void Configure(EntityTypeBuilder<Module> builder)
    {
        builder.HasKey(m => m.Id);
        builder.HasIndex(m => m.Code).IsUnique();
        builder.Property(m => m.Status).HasConversion<string>().IsRequired();

        // NUEVO: Relación 1 a Muchos con Capacidades
        builder.HasMany(m => m.Capabilities)
               .WithOne()
               .HasForeignKey(mc => mc.ModuleId)
               .OnDelete(DeleteBehavior.Cascade);

        // Instruimos a EF Core para que asigne directamente al campo privado _capabilities
        builder.Metadata.FindNavigation(nameof(Module.Capabilities))!
               .SetPropertyAccessMode(PropertyAccessMode.Field);
    }
}