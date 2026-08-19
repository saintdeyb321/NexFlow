using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class LicenseConfiguration : IEntityTypeConfiguration<License>
{
    public void Configure(EntityTypeBuilder<License> builder)
    {
        builder.HasKey(l => l.Id);

        builder.Property(l => l.Type).HasConversion<string>().IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().IsRequired();

        // Mapeamos el DateRange a dos columnas distintas
        builder.OwnsOne(l => l.ValidityPeriod, vp =>
        {
            vp.Property(d => d.Start).HasColumnName("ValidFrom").IsRequired();
            vp.Property(d => d.End).HasColumnName("ValidTo").IsRequired();
        });

        // Configurar la lista privada _licenseModules
        builder.Metadata.FindNavigation(nameof(License.LicenseModules))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        // Relación 1 a N con LicenseModule
        builder.HasMany(l => l.LicenseModules)
            .WithOne()
            .HasForeignKey(lm => lm.LicenseId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}