using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(u => u.Id);

        // Convertimos el ValueObject 'Email' a string para guardarlo en BD
        builder.Property(u => u.Email)
               .HasConversion(
                   email => email.Value,
                   value => new Email(value))
               .IsRequired()
               .HasMaxLength(255);

        // RESTRICCIÓN UNIQUE: El motor de BD rebotará correos duplicados
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.FirstName).IsRequired().HasMaxLength(100);
        builder.Property(u => u.LastName).IsRequired().HasMaxLength(100);
    }
}