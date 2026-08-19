using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class MembershipConfiguration : IEntityTypeConfiguration<Membership>
{
    public void Configure(EntityTypeBuilder<Membership> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Role).HasConversion<string>().IsRequired();

        // Constraint Real: Un usuario solo puede tener un registro de membresía por Workspace
        builder.HasIndex(m => new { m.UserId, m.WorkspaceId }).IsUnique();
    }
}