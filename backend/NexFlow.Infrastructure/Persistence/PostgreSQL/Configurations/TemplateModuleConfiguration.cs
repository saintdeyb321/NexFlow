using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using NexFlow.Domain.Entities;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Configurations;

public class TemplateModuleConfiguration : IEntityTypeConfiguration<TemplateModule>
{
    public void Configure(EntityTypeBuilder<TemplateModule> builder)
    {
        // RESTRICCIÓN UNIQUE (Llave Compuesta): La combinación Plantilla + Módulo es única
        builder.HasKey(tm => new { tm.TemplateId, tm.ModuleId });

        // Configuramos las relaciones (Foreign Keys)
        builder.HasOne<Template>()
               .WithMany()
               .HasForeignKey(tm => tm.TemplateId)
               .OnDelete(DeleteBehavior.Cascade); // Si borras la plantilla, se borra la relación

        builder.HasOne<Module>()
               .WithMany()
               .HasForeignKey(tm => tm.ModuleId)
               .OnDelete(DeleteBehavior.Restrict);
    }
}