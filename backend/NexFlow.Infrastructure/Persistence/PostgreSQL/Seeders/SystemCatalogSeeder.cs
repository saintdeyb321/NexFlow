using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Seeders;

public static class SystemCatalogSeeder
{
    public static async Task SeedCatalogAsync(NexFlowDbContext context)
    {
        // 1. Módulos Core (Inyección Idempotente usando Code)
        var coreModules = new[]
        {
            new { Code = "BUSINESS_PROFILE", Name = "Perfil del Negocio" },
            new { Code = "LOCATIONS", Name = "Gestión de Sedes" },
            new { Code = "BUSINESS_HOURS", Name = "Horarios de Atención" },
            new { Code = "SERVICES", Name = "Catálogo de Servicios" },
            new { Code = "FAQ", Name = "Base de Conocimiento" },
            new { Code = "RESERVATIONS", Name = "Motor de Reservas" },
            new { Code = "CUSTOMERS", Name = "Directorio de Clientes" }
        };

        var existingModules = await context.Modules.ToListAsync();
        foreach (var m in coreModules)
        {
            if (!existingModules.Any(x => x.Code == m.Code))
            {
                context.Modules.Add(Module.Create(m.Code, m.Name));
            }
        }
        await context.SaveChangesAsync();

        // 2. Plantillas Base (Inyección Idempotente usando Name)
        var templates = new[] { "SECRETARY", "RECEPTIONIST" };
        var existingTemplates = await context.Templates.ToListAsync();

        foreach (var t in templates)
        {
            // CORRECCIÓN AQUÍ: Usamos x.Name en lugar de x.Code para Template
            if (!existingTemplates.Any(x => x.Name == t))
            {
                context.Templates.Add(Template.Create(t));
            }
        }
        await context.SaveChangesAsync();

        // 3. Relaciones TemplateModules (Inyección Idempotente)
        existingModules = await context.Modules.ToListAsync();
        existingTemplates = await context.Templates.ToListAsync();
        var existingTemplateModules = await context.TemplateModules.ToListAsync();

        var templateConfig = new Dictionary<string, string[]>
        {
            { "SECRETARY", new[] { "BUSINESS_PROFILE", "FAQ", "SERVICES", "RESERVATIONS" } },
            { "RECEPTIONIST", new[] { "BUSINESS_PROFILE", "FAQ", "SERVICES", "RESERVATIONS", "CUSTOMERS" } }
        };

        foreach (var config in templateConfig)
        {
            // Usamos t.Name para buscar la plantilla
            var template = existingTemplates.FirstOrDefault(t => t.Name == config.Key);
            if (template == null) continue;

            foreach (var modCode in config.Value)
            {
                var mod = existingModules.FirstOrDefault(m => m.Code == modCode);
                if (mod != null && !existingTemplateModules.Any(tm => tm.TemplateId == template.Id && tm.ModuleId == mod.Id))
                {
                    context.TemplateModules.Add(new TemplateModule(template.Id, mod.Id));
                }
            }
        }
        await context.SaveChangesAsync();
    }
}