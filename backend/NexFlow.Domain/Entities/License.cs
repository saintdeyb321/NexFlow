using System;
using System.Collections.Generic;
using System.Linq;
using NexFlow.Domain.Enums;
using NexFlow.Domain.Exceptions;
using NexFlow.Domain.ValueObjects;

namespace NexFlow.Domain.Entities;

public class License : Entity
{
    public Guid WorkspaceId { get; private set; }
    public LicenseType Type { get; private set; }
    public LicenseStatus Status { get; private set; }
    public DateRange ValidityPeriod { get; private set; } = null!;
    public Guid? TemplateId { get; private set; }

    private readonly List<LicenseModule> _licenseModules = new();
    public IReadOnlyCollection<LicenseModule> LicenseModules => _licenseModules.AsReadOnly();

    private License() { }

    // 1. Licencia por Plantilla
    public static License CreateTemplateLicense(Guid workspaceId, Guid templateId, DateTime now, DateTime expiresAt)
    {
        if (expiresAt <= now) throw new DomainException("La fecha de expiración debe ser mayor a la fecha de inicio.");
        if (templateId == Guid.Empty) throw new DomainException("La plantilla es obligatoria.");

        return new License
        {
            WorkspaceId = workspaceId,
            Type = LicenseType.Template,
            TemplateId = templateId,
            Status = LicenseStatus.Active,
            ValidityPeriod = new DateRange(now, expiresAt)
        };
    }

    // 2. NUEVO: Licencia Personalizada (Custom)
    public static License CreateCustomLicense(Guid workspaceId, DateTime now, DateTime expiresAt)
    {
        if (expiresAt <= now) throw new DomainException("La fecha de expiración debe ser mayor a la fecha de inicio.");

        return new License
        {
            WorkspaceId = workspaceId,
            Type = LicenseType.Custom,
            TemplateId = null,
            Status = LicenseStatus.Active,
            ValidityPeriod = new DateRange(now, expiresAt)
        };
    }

    // 3. BLINDAJE: Evitar duplicados al agregar módulos
    public void AddModule(Guid moduleId)
    {
        if (Status != LicenseStatus.Active) throw new DomainException("Licencia inactiva.");
        if (moduleId == Guid.Empty) throw new DomainException("ID de módulo inválido.");

        // Regla de Negocio: No agregar si ya existe
        if (_licenseModules.Any(m => m.ModuleId == moduleId)) throw new DomainException("El módulo ya está asignado a esta licencia.");

        _licenseModules.Add(new LicenseModule(this.Id, moduleId));
    }

    public bool IsValidAt(DateTime currentDate)
    {
        return Status == LicenseStatus.Active &&
               currentDate >= ValidityPeriod.Start &&
               currentDate <= ValidityPeriod.End;
    }

    // 4. BLINDAJE: Renovación por duración, controlando fechas vencidas
    public void Renew(int durationInMonths, DateTime now)
    {
        if (durationInMonths <= 0) throw new DomainException("La duración debe ser mayor a cero.");

        DateTime newStart = ValidityPeriod.End > now ? ValidityPeriod.End : now;
        DateTime newEnd = newStart.AddMonths(durationInMonths);

        ValidityPeriod = new DateRange(ValidityPeriod.Start, newEnd);
        Status = LicenseStatus.Active;
    }

    public void Suspend() => Status = LicenseStatus.Suspended;
}