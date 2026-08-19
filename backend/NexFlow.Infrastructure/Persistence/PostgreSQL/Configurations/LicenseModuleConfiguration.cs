using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class LicenseModuleConfiguration : IEntityTypeConfiguration<LicenseModule>
{
    public void Configure(EntityTypeBuilder<LicenseModule> builder)
    {
        // Llave primaria compuesta porque es una tabla asociativa
        builder.HasKey(lm => new { lm.LicenseId, lm.ModuleId });
    }
}