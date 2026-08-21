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
        // 1. Crear Módulos Core si no existen
        if (!await context.Modules.AnyAsync())
        {
            var modules = new List<Module>
            {
                Module.Create("BUSINESS_PROFILE", "Perfil del Negocio"),
                Module.Create("LOCATIONS", "Gestión de Sedes"),
                Module.Create("BUSINESS_HOURS", "Horarios de Atención"),
                Module.Create("SERVICES", "Catálogo de Servicios"),
                Module.Create("FAQ", "Base de Conocimiento"),
                Module.Create("RESERVATIONS", "Motor de Reservas"),
                Module.Create("CUSTOMERS", "Directorio de Clientes")
            };

            await context.Modules.AddRangeAsync(modules);
            await context.SaveChangesAsync();
        }

        // 2. Crear Plantillas Base si no existen
        // 2. Crear Plantillas Base si no existen
        if (!await context.Templates.AnyAsync())
        {
            var secretaryTemplate = Template.Create("SECRETARY");
            var receptionistTemplate = Template.Create("RECEPTIONIST");

            await context.Templates.AddRangeAsync(secretaryTemplate, receptionistTemplate);
            await context.SaveChangesAsync();

            // 3. Relacionar Plantillas con Módulos (TemplateModules)
            var allModules = await context.Modules.ToListAsync();

            var secretaryModules = new[] { "BUSINESS_PROFILE", "FAQ", "SERVICES", "RESERVATIONS" };
            var receptionistModules = new[] { "BUSINESS_PROFILE", "FAQ", "SERVICES", "RESERVATIONS", "CUSTOMERS" };

            var templateModules = new List<TemplateModule>();

            // Usamos FirstOrDefault y Trim() para ignorar espacios fantasma o problemas de formato en la BD
            foreach (var modCode in secretaryModules)
            {
                var mod = allModules.FirstOrDefault(m => m.Code != null && m.Code.Trim().ToUpper() == modCode);
                if (mod != null)
                {
                    templateModules.Add(new TemplateModule(secretaryTemplate.Id, mod.Id));
                }
            }

            foreach (var modCode in receptionistModules)
            {
                var mod = allModules.FirstOrDefault(m => m.Code != null && m.Code.Trim().ToUpper() == modCode);
                if (mod != null)
                {
                    templateModules.Add(new TemplateModule(receptionistTemplate.Id, mod.Id));
                }
            }

            // Solo guardamos si logramos armar las relaciones
            if (templateModules.Any())
            {
                await context.TemplateModules.AddRangeAsync(templateModules);
                await context.SaveChangesAsync();
            }
        }
    }
}