using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Status).HasConversion<string>().IsRequired();

        // Npgsql maps uint row versions to PostgreSQL's system xmin column.
        builder.Property(r => r.RowVersion).IsRowVersion().HasColumnName("xmin");

        // RESTRICCIÓN DE HIERRO (Nivel BD): Evita doble reserva exacta al mismo tiempo
        builder.HasIndex(r => new { r.WorkspaceId, r.LocationId, r.ServiceId, r.StartTime }).IsUnique();
    }
}
