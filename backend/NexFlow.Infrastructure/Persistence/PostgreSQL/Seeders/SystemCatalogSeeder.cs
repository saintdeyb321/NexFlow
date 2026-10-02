using Microsoft.EntityFrameworkCore;
using NexFlow.Domain.Entities;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;

namespace NexFlow.Infrastructure.Persistence.PostgreSQL.Seeders;

public static class SystemCatalogSeeder
{
    public static async Task SeedCatalogAsync(NexFlowDbContext context)
    {
        // ============================================================
        // 1. MÓDULOS CORE
        // ============================================================

        var coreModules = new[]
        {
            new
            {
                Code = "BUSINESS_PROFILE",
                Name = "Perfil del Negocio",
                Desc = "Configuración general.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Leer perfil" },
                    new { Code = "UPDATE", Desc = "Actualizar perfil" }
                }
            },

            new
            {
                Code = "LOCATIONS",
                Name = "Gestión de Sedes",
                Desc = "Administración de locales.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Leer sedes" },
                    new { Code = "CREATE", Desc = "Crear sede" },
                    new { Code = "UPDATE", Desc = "Actualizar sede" },
                    new { Code = "DELETE", Desc = "Eliminar sede" }
                }
            },

            new
            {
                Code = "BUSINESS_HOURS",
                Name = "Horarios de Atención",
                Desc = "Control de disponibilidad.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Leer horarios" },
                    new { Code = "UPDATE", Desc = "Actualizar horarios" }
                }
            },

            new
            {
                Code = "FAQ",
                Name = "Base de Conocimiento",
                Desc = "Preguntas frecuentes para la IA.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Consultar FAQs" },
                    new { Code = "CREATE", Desc = "Crear FAQ" },
                    new { Code = "UPDATE", Desc = "Actualizar FAQ" },
                    new { Code = "DELETE", Desc = "Eliminar FAQ" }
                }
            },

            new
            {
                Code = "SERVICES",
                Name = "Catálogo de Servicios",
                Desc = "Servicios que ofrece el negocio.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Consultar servicios" },
                    new { Code = "CREATE", Desc = "Crear servicio" },
                    new { Code = "UPDATE", Desc = "Actualizar servicio" },
                    new { Code = "DELETE", Desc = "Eliminar servicio" },
                    new { Code = "GENERATE", Desc = "Generar artefacto de servicios" }
                }
            },

            new
            {
                Code = "CATALOG",
                Name = "Catálogo de Productos",
                Desc = "Productos físicos o consumibles.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Consultar productos" },
                    new { Code = "CREATE", Desc = "Crear producto" },
                    new { Code = "UPDATE", Desc = "Actualizar producto" },
                    new { Code = "DELETE", Desc = "Eliminar producto" },
                    new { Code = "GENERATE", Desc = "Generar artefacto de productos" }
                }
            },

            new
            {
                Code = "ORDERS",
                Name = "Gestión de Pedidos",
                Desc = "Coordinación y revisión de listas de compra.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Consultar pedidos" },
                    new { Code = "CREATE", Desc = "Crear pedido" },
                    new { Code = "UPDATE", Desc = "Actualizar pedido" },
                    new { Code = "UPDATE_STATUS", Desc = "Actualizar estado del pedido" }
                }
            },

            new
            {
                Code = "RESERVATIONS",
                Name = "Motor de Reservas",
                Desc = "Gestión de citas.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Leer reservas" },
                    new { Code = "UPDATE", Desc = "Editar reservas" },
                    new { Code = "COMPLETE", Desc = "Completar reservas" },
                    new { Code = "CHECK_AVAILABILITY", Desc = "Consultar horarios libres" },
                    new { Code = "CREATE", Desc = "Crear nueva reserva" },
                    new { Code = "CANCEL", Desc = "Cancelar reserva" }
                }
            },

            new
            {
                Code = "REQUESTS",
                Name = "Solicitudes",
                Desc = "Gestión de trámites y afiliaciones.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Leer solicitudes" },
                    new { Code = "ASSIGN", Desc = "Asignar solicitudes" },
                    new { Code = "CREATE", Desc = "Crear solicitud" },
                    new { Code = "UPDATE_STATUS", Desc = "Actualizar estado" }
                }
            },

            new
            {
                Code = "CONVERSATIONS",
                Name = "Bandeja de Entrada",
                Desc = "Inbox y control de chats.",
                Caps = new[]
                {
                    new { Code = "READ", Desc = "Leer chats" },
                    new { Code = "SEND_MESSAGE", Desc = "Enviar mensaje manual" },
                    new { Code = "TAKEOVER", Desc = "Asumir control humano" },
                    new { Code = "RELEASE", Desc = "Liberar chat" },
                    new { Code = "DELETE", Desc = "Eliminar chat" },
                    new { Code = "CONFIGURE", Desc = "Configurar conexión" }
                }
            }
        };

        // ============================================================
        // 2. CREAR ÚNICAMENTE LOS MÓDULOS QUE NO EXISTEN
        // ============================================================

        var existingModuleList = await context.Modules
            .AsNoTracking()
            .ToListAsync();

        var existingModuleCodes = existingModuleList
            .Select(m => m.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var modulesAdded = false;

        foreach (var definition in coreModules)
        {
            if (existingModuleCodes.Contains(definition.Code))
                continue;

            // Importante:
            // aquí creamos solamente el Module.
            // Las capabilities se reconciliarán después de tener
            // definitivamente todos los ModuleId persistidos.
            var module = Module.Create(
                definition.Code,
                definition.Name,
                definition.Desc);

            context.Modules.Add(module);
            modulesAdded = true;
        }

        if (modulesAdded)
        {
            await context.SaveChangesAsync();
        }

        // Evita conservar tracking innecesario entre las etapas del seeder.
        context.ChangeTracker.Clear();

        // ============================================================
        // 3. RECONCILIAR CAPABILITIES
        // ============================================================

        var persistedModules = await context.Modules
            .AsNoTracking()
            .ToListAsync();

        var modulesByCode = persistedModules
            .ToDictionary(
                m => m.Code,
                StringComparer.OrdinalIgnoreCase);

        var persistedCapabilities = await context.ModuleCapabilities
            .AsNoTracking()
            .Select(c => new
            {
                c.ModuleId,
                c.Code
            })
            .ToListAsync();

        var capabilityKeys = persistedCapabilities
            .Select(c => (
                c.ModuleId,
                Code: c.Code.ToUpperInvariant()))
            .ToHashSet();

        var capabilitiesAdded = false;

        foreach (var definition in coreModules)
        {
            if (!modulesByCode.TryGetValue(definition.Code, out var module))
            {
                throw new InvalidOperationException(
                    $"No se pudo encontrar el módulo persistido '{definition.Code}'.");
            }

            foreach (var capabilityDefinition in definition.Caps)
            {
                var normalizedCode =
                    capabilityDefinition.Code.ToUpperInvariant();

                var key = (module.Id, normalizedCode);

                if (!capabilityKeys.Add(key))
                    continue;

                /*
                 * IMPORTANTE:
                 *
                 * No usamos:
                 *
                 * module.AddCapability(...)
                 *
                 * sobre un Module existente y tracked.
                 *
                 * ModuleCapability tiene un Guid generado desde dominio,
                 * mientras EF lo considera ValueGeneratedOnAdd.
                 *
                 * Añadiéndolo explícitamente al DbSet garantizamos que
                 * EF lo marque como EntityState.Added y ejecute INSERT.
                 */
                var capability = new ModuleCapability(
                    module.Id,
                    normalizedCode,
                    capabilityDefinition.Desc);

                context.ModuleCapabilities.Add(capability);

                capabilitiesAdded = true;
            }
        }

        if (capabilitiesAdded)
        {
            await context.SaveChangesAsync();
        }

        context.ChangeTracker.Clear();

        // ============================================================
        // 4. PLANTILLAS
        // ============================================================

        var templates = new[]
        {
            new
            {
                Code = "SUPPORT",
                Name = "Atención Básica",
                Desc = "Respuestas automáticas e información general."
            },
            new
            {
                Code = "BOOKING",
                Name = "Asistente de Reservas",
                Desc = "Ideal para consultorios, spas y salones."
            },
            new
            {
                Code = "COMMERCIAL",
                Name = "Asistente Comercial",
                Desc = "Ideal para pastelerías, tiendas y retail."
            },
            new
            {
                Code = "REQUESTS",
                Name = "Asistente de Trámites",
                Desc = "Gestión de afiliaciones, soporte o solicitudes."
            },
            new
            {
                Code = "FULL",
                Name = "Operaciones Completas",
                Desc = "Todas las capacidades operativas del sistema."
            }
        };

        var existingTemplateList = await context.Templates
            .AsNoTracking()
            .ToListAsync();

        var existingTemplateCodes = existingTemplateList
            .Select(t => t.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var templatesAdded = false;

        foreach (var definition in templates)
        {
            if (existingTemplateCodes.Contains(definition.Code))
                continue;

            context.Templates.Add(
                Template.Create(
                    definition.Code,
                    definition.Name,
                    definition.Desc));

            templatesAdded = true;
        }

        if (templatesAdded)
        {
            await context.SaveChangesAsync();
        }

        context.ChangeTracker.Clear();

        // ============================================================
        // 5. RECONCILIAR TEMPLATE -> MODULE
        // ============================================================

        persistedModules = await context.Modules
            .AsNoTracking()
            .ToListAsync();

        var persistedTemplates = await context.Templates
            .AsNoTracking()
            .ToListAsync();

        modulesByCode = persistedModules
            .ToDictionary(
                m => m.Code,
                StringComparer.OrdinalIgnoreCase);

        var templatesByCode = persistedTemplates
            .ToDictionary(
                t => t.Code,
                StringComparer.OrdinalIgnoreCase);

        var existingTemplateModules = await context.TemplateModules
            .AsNoTracking()
            .Select(tm => new
            {
                tm.TemplateId,
                tm.ModuleId
            })
            .ToListAsync();

        var templateModuleKeys = existingTemplateModules
            .Select(tm => (tm.TemplateId, tm.ModuleId))
            .ToHashSet();

        var templateConfig = new Dictionary<string, string[]>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["SUPPORT"] =
            [
                "BUSINESS_PROFILE",
                "LOCATIONS",
                "BUSINESS_HOURS",
                "FAQ",
                "CONVERSATIONS"
            ],

            ["BOOKING"] =
            [
                "BUSINESS_PROFILE",
                "LOCATIONS",
                "BUSINESS_HOURS",
                "FAQ",
                "SERVICES",
                "RESERVATIONS",
                "CONVERSATIONS"
            ],

            ["COMMERCIAL"] =
            [
                "BUSINESS_PROFILE",
                "LOCATIONS",
                "BUSINESS_HOURS",
                "FAQ",
                "CATALOG",
                "ORDERS",
                "CONVERSATIONS"
            ],

            ["REQUESTS"] =
            [
                "BUSINESS_PROFILE",
                "LOCATIONS",
                "BUSINESS_HOURS",
                "FAQ",
                "REQUESTS",
                "CONVERSATIONS"
            ],

            ["FULL"] =
            [
                "BUSINESS_PROFILE",
                "LOCATIONS",
                "BUSINESS_HOURS",
                "FAQ",
                "SERVICES",
                "CATALOG",
                "ORDERS",
                "RESERVATIONS",
                "REQUESTS",
                "CONVERSATIONS"
            ]
        };

        var templateModulesAdded = false;

        foreach (var config in templateConfig)
        {
            if (!templatesByCode.TryGetValue(
                    config.Key,
                    out var template))
            {
                continue;
            }

            foreach (var moduleCode in config.Value)
            {
                if (!modulesByCode.TryGetValue(
                        moduleCode,
                        out var module))
                {
                    continue;
                }

                var key = (template.Id, module.Id);

                if (!templateModuleKeys.Add(key))
                    continue;

                context.TemplateModules.Add(
                    new TemplateModule(
                        template.Id,
                        module.Id));

                templateModulesAdded = true;
            }
        }

        if (templateModulesAdded)
        {
            await context.SaveChangesAsync();
        }

        context.ChangeTracker.Clear();

        // ============================================================
        // 6. WORKSPACE INTERNO NEXFLOW
        // ============================================================

        var internalWorkspace = await context.Workspaces
            .AsNoTracking()
            .FirstOrDefaultAsync(
                w => w.Name == "NexFlow Internal");

        Guid internalWorkspaceId;

        if (internalWorkspace == null)
        {
            var newInternalWorkspace =
                Workspace.Create("NexFlow Internal");

            newInternalWorkspace.Activate();

            context.Workspaces.Add(newInternalWorkspace);

            await context.SaveChangesAsync();

            internalWorkspaceId = newInternalWorkspace.Id;

            context.ChangeTracker.Clear();
        }
        else
        {
            internalWorkspaceId = internalWorkspace.Id;
        }

        // ============================================================
        // 7. LICENCIA INTERNA
        // ============================================================

        var internalLicense = await context.Licenses
            .IgnoreQueryFilters()
            .Include(l => l.LicenseModules)
            .FirstOrDefaultAsync(
                l => l.WorkspaceId == internalWorkspaceId);

        persistedModules = await context.Modules
            .AsNoTracking()
            .ToListAsync();

        if (internalLicense == null)
        {
            internalLicense = License.CreateCustomLicense(
                internalWorkspaceId,
                DateTime.UtcNow,
                null,
                999);

            foreach (var module in persistedModules)
            {
                internalLicense.AddCustomModule(module.Id);
            }

            context.Licenses.Add(internalLicense);

            await context.SaveChangesAsync();

            context.ChangeTracker.Clear();
        }
        else
        {
            var licenseModulesAdded = false;

            foreach (var module in persistedModules)
            {
                if (internalLicense.LicenseModules.Any(
                        lm => lm.ModuleId == module.Id))
                {
                    continue;
                }

                internalLicense.AddCustomModule(module.Id);

                /*
                 * LicenseModule utiliza PK compuesta.
                 * Normalmente EF ya lo detectará como Added,
                 * pero lo marcamos explícitamente para que el Seeder
                 * sea determinista y no dependa del tracking del graph.
                 */
                var addedLicenseModule = internalLicense.LicenseModules
                    .Single(lm => lm.ModuleId == module.Id);

                context.Entry(addedLicenseModule).State =
                    EntityState.Added;

                licenseModulesAdded = true;
            }

            if (licenseModulesAdded)
            {
                await context.SaveChangesAsync();
            }

            context.ChangeTracker.Clear();
        }
    }
}