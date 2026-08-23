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

    // NUEVO: El control estricto de sedes
    public int MaxLocations { get; private set; }

    private readonly List<LicenseModule> _licenseModules = new();
    public IReadOnlyCollection<LicenseModule> LicenseModules => _licenseModules.AsReadOnly();

    private License() { }

    public static License CreateTemplateLicense(Guid workspaceId, Guid templateId, DateTime now, DateTime expiresAt, int maxLocations)
    {
        if (expiresAt <= now) throw new DomainException("La fecha de expiración debe ser mayor a la fecha de inicio.");
        if (templateId == Guid.Empty) throw new DomainException("La plantilla es obligatoria.");
        if (maxLocations < 1) throw new DomainException("La licencia debe permitir al menos 1 sede operativa.");

        return new License
        {
            WorkspaceId = workspaceId,
            Type = LicenseType.Template,
            TemplateId = templateId,
            Status = LicenseStatus.Active,
            ValidityPeriod = new DateRange(now, expiresAt),
            MaxLocations = maxLocations
        };
    }

    public static License CreateCustomLicense(Guid workspaceId, DateTime now, DateTime expiresAt, int maxLocations)
    {
        if (expiresAt <= now) throw new DomainException("La fecha de expiración debe ser mayor a la fecha de inicio.");
        if (maxLocations < 1) throw new DomainException("La licencia debe permitir al menos 1 sede operativa.");

        return new License
        {
            WorkspaceId = workspaceId,
            Type = LicenseType.Custom,
            TemplateId = null,
            Status = LicenseStatus.Active,
            ValidityPeriod = new DateRange(now, expiresAt),
            MaxLocations = maxLocations
        };
    }

    public void AddTemplateModule(Guid moduleId)
    {
        if (Type != LicenseType.Template) throw new DomainException("Solo aplicable a licencias fijas por plantilla.");
        AddModuleInternal(moduleId);
    }

    public void AddCustomModule(Guid moduleId)
    {
        if (Type != LicenseType.Custom) throw new DomainException("No se pueden agregar módulos a la carta en una licencia de plantilla fija.");
        AddModuleInternal(moduleId);
    }

    private void AddModuleInternal(Guid moduleId)
    {
        if (Status != LicenseStatus.Active) throw new DomainException("Licencia inactiva.");
        if (moduleId == Guid.Empty) throw new DomainException("ID de módulo inválido.");
        if (_licenseModules.Any(m => m.ModuleId == moduleId)) throw new DomainException("El módulo ya está asignado a esta licencia.");

        _licenseModules.Add(new LicenseModule(this.Id, moduleId));
    }

    public bool IsValidAt(DateTime currentDate) => Status == LicenseStatus.Active && currentDate >= ValidityPeriod.Start && currentDate <= ValidityPeriod.End;

    public void Renew(int durationInMonths, DateTime now)
    {
        if (durationInMonths <= 0) throw new DomainException("La duración debe ser mayor a cero.");
        DateTime newStart = ValidityPeriod.End > now ? ValidityPeriod.End : now;
        ValidityPeriod = new DateRange(ValidityPeriod.Start, newStart.AddMonths(durationInMonths));
        Status = LicenseStatus.Active;
    }

    public void Suspend() => Status = LicenseStatus.Suspended;

    // Opcional: Si en el futuro quieres que el SuperAdmin aumente las sedes de un cliente
    public void UpdateMaxLocations(int newMaxLocations)
    {
        if (newMaxLocations < 1) throw new DomainException("La licencia debe permitir al menos 1 sede operativa.");
        MaxLocations = newMaxLocations;
    }
}