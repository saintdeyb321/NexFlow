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

    // Crear licencia por Plantilla
    public static License CreateTemplateLicense(Guid workspaceId, Guid templateId, DateTime startDate, DateTime endDate)
    {
        return new License
        {
            WorkspaceId = workspaceId,
            Type = LicenseType.Template,
            Status = LicenseStatus.Active,
            ValidityPeriod = new DateRange(startDate, endDate),
            TemplateId = templateId
        };
    }

    // Crear licencia Custom
    public static License CreateCustomLicense(Guid workspaceId, DateTime startDate, DateTime endDate)
    {
        return new License
        {
            WorkspaceId = workspaceId,
            Type = LicenseType.Custom,
            Status = LicenseStatus.Active,
            ValidityPeriod = new DateRange(startDate, endDate),
            TemplateId = null
        };
    }

    // --- REGLAS DE NEGOCIO ---

    public void Extend(DateTime newExpirationDate)
    {
        if (Status is LicenseStatus.Cancelled)
            throw new DomainException("No se puede extender una licencia cancelada.");

        ValidityPeriod = ValidityPeriod.Extend(newExpirationDate);
        Status = ValidityPeriod.IsActive(DateTime.UtcNow) ? LicenseStatus.Active : Status;
        UpdateTimestamp();
    }

    public void Suspend()
    {
        Status = LicenseStatus.Suspended;
        UpdateTimestamp();
    }

    public void Reactivate()
    {
        if (Status is not LicenseStatus.Suspended and not LicenseStatus.Expired)
            throw new DomainException("Solo se pueden reactivar licencias suspendidas o expiradas.");

        if (!ValidityPeriod.IsActive(DateTime.UtcNow))
            throw new DomainException("No se puede reactivar porque la fecha actual está fuera del periodo de validez. Extienda la licencia primero.");

        Status = LicenseStatus.Active;
        UpdateTimestamp();
    }

    public void ChangeTemplate(Guid newTemplateId)
    {
        if (Type != LicenseType.Template)
            throw new DomainException("Solo las licencias de tipo Template pueden cambiar de plantilla.");

        TemplateId = newTemplateId;
        UpdateTimestamp();
    }
}