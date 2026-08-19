using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class TemplateModuleConfiguration : IEntityTypeConfiguration<TemplateModule>
{
    public void Configure(EntityTypeBuilder<TemplateModule> builder)
    {
        // Llave primaria compuesta para la tabla asociativa
        builder.HasKey(tm => new { tm.TemplateId, tm.ModuleId });
    }
}