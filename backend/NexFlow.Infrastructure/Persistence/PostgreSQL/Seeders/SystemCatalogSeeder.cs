using Microsoft.EntityFrameworkCore;
using NexFlow.Domain.Entities;
using NexFlow.Domain.Enums;
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
            new { Code = "FAQ", Name = "Base de Conocimiento", Desc = "Preguntas frecuentes para la IA.", Caps = new[] { new { Code = "READ", Desc = "Consultar FAQs" } } },

            new { Code = "SERVICES", Name = "Catálogo de Servicios", Desc = "Servicios que ofrece el negocio.", Caps = new[] { new { Code = "READ", Desc = "Consultar servicios" } } },
            new { Code = "CATALOG", Name = "Catálogo de Productos", Desc = "Productos físicos o consumibles.", Caps = new[] { new { Code = "READ", Desc = "Consultar productos" } } },

            new { Code = "RESERVATIONS", Name = "Motor de Reservas", Desc = "Gestión de citas.", Caps = new[] {
                new { Code = "CHECK_AVAILABILITY", Desc = "Consultar horarios libres" },
                new { Code = "CREATE", Desc = "Crear nueva reserva" },
                new { Code = "CANCEL", Desc = "Cancelar reserva" }
            } },

            new { Code = "REQUESTS", Name = "Solicitudes", Desc = "Gestión de trámites y afiliaciones.", Caps = new[] {
                new { Code = "CREATE", Desc = "Crear solicitud" },
                new { Code = "UPDATE_STATUS", Desc = "Actualizar estado" }
            } },

            new { Code = "CONVERSATIONS", Name = "Bandeja de Entrada", Desc = "Inbox y control de chats.", Caps = new[] {
                new { Code = "READ", Desc = "Leer chats" },
                new { Code = "SEND_MESSAGE", Desc = "Enviar mensaje manual" },
                new { Code = "TAKEOVER", Desc = "Asumir control humano" }
            } }
            // 🔥 SPRINT 4.3: Eliminamos "NOTIFICATIONS" y "CUSTOMERS" del catálogo comercial para no vender aire.
        };

        var existingModules = await context.Modules.Include(m => m.Capabilities).ToListAsync();

        foreach (var m in coreModules)
        {
            var module = existingModules.FirstOrDefault(x => x.Code == m.Code);

            if (module == null)
            {
                module = Module.Create(m.Code, m.Name, m.Desc);
                foreach (var cap in m.Caps)
                {
                    module.AddCapability(cap.Code, cap.Desc);
                }
                context.Modules.Add(module);
            }
            else
            {
                foreach (var cap in m.Caps)
                {
                    if (!module.Capabilities.Any(c => c.Code == cap.Code))
                    {
                        module.AddCapability(cap.Code, cap.Desc);
                    }
                }
            }
        }
        await context.SaveChangesAsync();

        // 2. Las Plantillas Estratégicas
        var templates = new[]
        {
            new { Code = "SUPPORT", Name = "Atención Básica", Desc = "Respuestas automáticas e información general." },
            new { Code = "BOOKING", Name = "Asistente de Reservas", Desc = "Ideal para consultorios, spas y salones." },
            new { Code = "COMMERCIAL", Name = "Asistente Comercial", Desc = "Ideal para pastelerías, tiendas y retail." },
            new { Code = "REQUESTS", Name = "Asistente de Trámites", Desc = "Gestión de afiliaciones, soporte o solicitudes." },
            new { Code = "FULL", Name = "Operaciones Completas", Desc = "Todas las capacidades operativas del sistema." }
        };

        var existingTemplates = await context.Templates.ToListAsync();
        foreach (var t in templates)
        {
            if (!existingTemplates.Any(x => x.Code == t.Code))
            {
                context.Templates.Add(Template.Create(t.Code, t.Name, t.Desc));
            }
        }
        await context.SaveChangesAsync();

        existingModules = await context.Modules.ToListAsync();
        existingTemplates = await context.Templates.ToListAsync();
        var existingTemplateModules = await context.TemplateModules.ToListAsync();

        // 🔥 SPRINT 4.3: Depuramos las plantillas para no amarrar módulos inexistentes
        var templateConfig = new Dictionary<string, string[]>
        {
            { "SUPPORT", new[] { "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "FAQ", "CONVERSATIONS" } },
            { "BOOKING", new[] { "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "FAQ", "SERVICES", "RESERVATIONS", "CONVERSATIONS" } },
            { "COMMERCIAL", new[] { "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "FAQ", "CATALOG", "CONVERSATIONS" } },
            { "REQUESTS", new[] { "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "FAQ", "REQUESTS", "CONVERSATIONS" } },
            { "FULL", new[] { "BUSINESS_PROFILE", "LOCATIONS", "BUSINESS_HOURS", "FAQ", "SERVICES", "CATALOG", "RESERVATIONS", "REQUESTS", "CONVERSATIONS" } }
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

        // 🔥 SPRINT 4.4: Reconciliación total del Seeder del SuperAdmin
        var internalWorkspace = await context.Workspaces.FirstOrDefaultAsync(w => w.Name == "NexFlow Internal");
        if (internalWorkspace == null)
        {
            internalWorkspace = Workspace.Create("NexFlow Internal");
            internalWorkspace.Activate();
            context.Workspaces.Add(internalWorkspace);
            await context.SaveChangesAsync();

            var license = License.CreateCustomLicense(
                internalWorkspace.Id,
                DateTime.UtcNow,
                null, // SuperAdmin no expira
                999
            );

            foreach (var mod in existingModules)
            {
                license.AddCustomModule(mod.Id);
            }

            context.Licenses.Add(license);
            await context.SaveChangesAsync();
        }
        else
        {
            // Reconciliación: Asegurar que la licencia interna siempre tiene TODOS los módulos vigentes
            var license = await context.Licenses.Include(l => l.LicenseModules).FirstOrDefaultAsync(l => l.WorkspaceId == internalWorkspace.Id);
            if (license != null)
            {
                foreach (var mod in existingModules)
                {
                    if (!license.LicenseModules.Any(lm => lm.ModuleId == mod.Id))
                    {
                        license.AddCustomModule(mod.Id);
                    }
                }
                await context.SaveChangesAsync();
            }
        }

        // Vincular a los SuperAdmins
        var sysAdmins = await context.SystemAdministrators.ToListAsync();
        foreach (var admin in sysAdmins)
        {
            var isMember = await context.Memberships.AnyAsync(m => m.UserId == admin.UserId && m.WorkspaceId == internalWorkspace.Id);
            if (!isMember)
            {
                var membership = Membership.Create(admin.UserId, internalWorkspace.Id, MembershipRole.Owner);
                context.Memberships.Add(membership);
            }
        }
        await context.SaveChangesAsync();
    }
}