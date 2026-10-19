using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities.System;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public sealed class ReservationResourceStateConfiguration : IEntityTypeConfiguration<ReservationResourceState>
{
    public void Configure(EntityTypeBuilder<ReservationResourceState> builder)
    {
        builder.ToTable("ReservationResourceStates");
        builder.HasKey(r => new { r.WorkspaceId, r.Kind, r.ResourceId });
        builder.Property(r => r.Kind).HasMaxLength(16);
        builder.HasOne<NexFlow.Domain.Entities.Workspace>().WithMany().HasForeignKey(r => r.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(r => r.PayloadJson).HasColumnType("jsonb");
        builder.HasIndex(r => r.NextAttemptAt).HasFilter("\"PendingOperationId\" IS NOT NULL");
    }
}
