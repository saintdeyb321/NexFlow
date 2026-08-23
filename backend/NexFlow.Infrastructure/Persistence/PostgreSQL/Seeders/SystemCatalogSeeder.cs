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
        // 1. Módulos Core con sus Capacidades Granulares
        var coreModules = new[]
        {
            new { Code = "BUSINESS_PROFILE", Name = "Perfil del Negocio", Desc = "Configuración general.", Caps = new[] { new { Code = "READ", Desc = "Leer perfil" } } },
            new { Code = "LOCATIONS", Name = "Gestión de Sedes", Desc = "Administración de locales.", Caps = new[] { new { Code = "READ", Desc = "Leer sedes" } } },
            new { Code = "BUSINESS_HOURS", Name = "Horarios de Atención", Desc = "Control de disponibilidad.", Caps = new[] { new { Code = "READ", Desc = "Leer horarios" } } },
            new { Code = "SERVICES", Name = "Catálogo de Servicios", Desc = "Servicios que ofrece el negocio.", Caps = new[] { new { Code = "READ", Desc = "Consultar catálogo" } } },
            new { Code = "FAQ", Name = "Base de Conocimiento", Desc = "Preguntas frecuentes para la IA.", Caps = new[] { new { Code = "READ", Desc = "Consultar FAQs" } } },

            new { Code = "RESERVATIONS", Name = "Motor de Reservas", Desc = "Gestión de citas.", Caps = new[] {
                new { Code = "CHECK_AVAILABILITY", Desc = "Consultar horarios libres" },
                new { Code = "CREATE", Desc = "Crear nueva reserva" },
                new { Code = "CANCEL", Desc = "Cancelar reserva" }
            } },

            new { Code = "CUSTOMERS", Name = "Contactos", Desc = "Identidad de consumidores.", Caps = new[] { new { Code = "READ", Desc = "Ver contactos" } } },

            new { Code = "CONVERSATIONS", Name = "Bandeja de Entrada", Desc = "Inbox y control de chats.", Caps = new[] {
                new { Code = "READ", Desc = "Leer chats" },
                new { Code = "SEND_MESSAGE", Desc = "Enviar mensaje manual" },
                new { Code = "TAKEOVER", Desc = "Asumir control humano" }
            } },

            new { Code = "REQUESTS", Name = "Solicitudes", Desc = "Gestión de procesos operativos.", Caps = new[] {
                new { Code = "CREATE", Desc = "Crear solicitud" },
                new { Code = "UPDATE_STATUS", Desc = "Actualizar estado" }
            } }
        };

        // NUEVO: Incluimos las capacidades al consultar para no duplicarlas
        var existingModules = await context.Modules.Include(m => m.Capabilities).ToListAsync();

        foreach (var m in coreModules)
        {
            var module = existingModules.FirstOrDefault(x => x.Code == m.Code);
            if (module == null)
            {
                module = Module.Create(m.Code, m.Name, m.Desc);
                context.Modules.Add(module);
            }

            // Inyectamos las capacidades mediante la entidad (que ya valida duplicados internamente)
            foreach (var cap in m.Caps)
            {
                module.AddCapability(cap.Code, cap.Desc);
            }
        }
        await context.SaveChangesAsync();

        // 2. Las 5 Plantillas Estratégicas del MVP (Se mantiene intacto)
        var templates = new[]
        {
            new { Code = "SECRETARY", Name = "Secretaria / Agenda", Desc = "Ideal para consultorios y profesionales." },
            new { Code = "RECEPTIONIST", Name = "Recepción", Desc = "Ideal para hoteles y centros médicos." },
            new { Code = "COMMERCIAL", Name = "Atención Comercial", Desc = "Para agencias, distribuidores y talleres." },
            new { Code = "SERVICE_REQUEST", Name = "Solicitud / Trámite", Desc = "Procesos para incorporar unidades o recursos." },
            new { Code = "OPERATIONS", Name = "Operaciones", Desc = "Para servicios de mantenimiento y alquileres." }
        };

        var existingTemplates = await context.Templates.ToListAsync();
        foreach (var t in templates)
        {
            if (!existingTemplates.Any(x => x.Code == t.Code)) context.Templates.Add(Template.Create(t.Code, t.Name, t.Desc));
        }
        await context.SaveChangesAsync();

        // 3. Relaciones Template-Modules (Se mantiene intacto)
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