using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.HasKey(r => r.Id);

        // Forzamos que el Enum se guarde como string para que sea legible en BD
        builder.Property(r => r.Status).HasConversion<string>().IsRequired();

        // RESTRICCIÓN DE HIERRO: Evita duplicaciones exactas. 
        // Nadie puede reservar el mismo recurso a la misma hora en la misma sede.
        builder.HasIndex(r => new { r.WorkspaceId, r.LocationId, r.ServiceId, r.StartTime }).IsUnique();
    }
}