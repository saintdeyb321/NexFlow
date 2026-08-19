using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.HasKey(m => m.Id);

        // RESTRICCIÓN UNIQUE: Un usuario solo puede tener UNA membresía por Workspace
        builder.HasIndex(m => new { m.UserId, m.WorkspaceId }).IsUnique();

        builder.Property(m => m.Role).IsRequired();
    }
}