using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Entities.System;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public sealed class WhatsAppConnectionConfiguration : IEntityTypeConfiguration<WhatsAppConnection>
{
    public void Configure(EntityTypeBuilder<WhatsAppConnection> builder)
    {
        builder.HasKey(c => c.WorkspaceId);
        builder.HasOne<Workspace>().WithOne().HasForeignKey<WhatsAppConnection>(c => c.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(c => c.Status).HasMaxLength(32).IsRequired();
        builder.Property(c => c.LoggedOutOwner).HasMaxLength(128);
    }
}
