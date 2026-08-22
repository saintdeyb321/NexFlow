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
        // 1. Módulos Core (Agregamos CONVERSATIONS y REQUESTS para el MVP)
        var coreModules = new[]
        {
            new { Code = "BUSINESS_PROFILE", Name = "Perfil del Negocio", Desc = "Configuración general de la empresa." },
            new { Code = "LOCATIONS", Name = "Gestión de Sedes", Desc = "Administración de múltiples locales." },
            new { Code = "BUSINESS_HOURS", Name = "Horarios de Atención", Desc = "Control de disponibilidad y apertura." },
            new { Code = "SERVICES", Name = "Catálogo de Servicios", Desc = "Servicios que ofrece el negocio." },
            new { Code = "FAQ", Name = "Base de Conocimiento", Desc = "Preguntas frecuentes para la IA." },
            new { Code = "RESERVATIONS", Name = "Motor de Reservas", Desc = "Gestión de citas y agendamiento." },
            new { Code = "CUSTOMERS", Name = "Directorio de Clientes", Desc = "Historial de contactos y consumidores." },
            new { Code = "CONVERSATIONS", Name = "Bandeja de Entrada", Desc = "Inbox híbrido y control de chats de WhatsApp." },
            new { Code = "REQUESTS", Name = "Solicitudes y Trámites", Desc = "Gestión de procesos operativos (Ej: Flotas)." }
        };

        var existingModules = await context.Modules.ToListAsync();
        foreach (var m in coreModules)
        {
            // Idempotencia segura buscando por Code
            if (!existingModules.Any(x => x.Code == m.Code))
            {
                context.Modules.Add(Module.Create(m.Code, m.Name, m.Desc));
            }
        }
        await context.SaveChangesAsync();

        // 2. Las 5 Plantillas Estratégicas del MVP
        var templates = new[]
        {
            new { Code = "SECRETARY", Name = "Secretaria / Agenda", Desc = "Ideal para consultorios, estudios y profesionales." },
            new { Code = "RECEPTIONIST", Name = "Recepción", Desc = "Ideal para hoteles, spas y centros médicos." },
            new { Code = "COMMERCIAL", Name = "Atención Comercial", Desc = "Para agencias, distribuidores y talleres." },
            new { Code = "SERVICE_REQUEST", Name = "Solicitud / Trámite", Desc = "Procesos para incorporar unidades o recursos." },
            new { Code = "OPERATIONS", Name = "Operaciones", Desc = "Para servicios de mantenimiento y alquileres." }
        };

        var existingTemplates = await context.Templates.ToListAsync();
        foreach (var t in templates)
        {
            if (!existingTemplates.Any(x => x.Code == t.Code))
            {
                // Ahora usamos la firma correcta: Code, Name, Description
                context.Templates.Add(Template.Create(t.Code, t.Name, t.Desc));
            }
        }
        await context.SaveChangesAsync();

        // 3. Relaciones Template-Modules (El armado de las plantillas)
        existingModules = await context.Modules.ToListAsync();
        existingTemplates = await context.Templates.ToListAsync();
        var existingTemplateModules = await context.TemplateModules.ToListAsync();

        var templateConfig = new Dictionary<string, string[]>
        {
            { "SECRETARY", new[] { "BUSINESS_PROFILE", "FAQ", "SERVICES", "CUSTOMERS", "RESERVATIONS", "CONVERSATIONS" } },
            { "RECEPTIONIST", new[] { "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "SERVICES", "CUSTOMERS", "RESERVATIONS", "FAQ", "CONVERSATIONS" } },
            { "COMMERCIAL", new[] { "BUSINESS_PROFILE", "SERVICES", "FAQ", "CUSTOMERS", "CONVERSATIONS" } },
            { "SERVICE_REQUEST", new[] { "BUSINESS_PROFILE", "SERVICES", "FAQ", "CUSTOMERS", "CONVERSATIONS", "REQUESTS" } },
            { "OPERATIONS", new[] { "BUSINESS_PROFILE", "LOCATIONS", "SERVICES", "CUSTOMERS", "RESERVATIONS", "FAQ", "CONVERSATIONS" } }
        };

        foreach (var config in templateConfig)
        {
            var template = existingTemplates.FirstOrDefault(t => t.Code == config.Key);
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