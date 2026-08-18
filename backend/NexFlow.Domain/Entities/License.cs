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

    public static License CreateTemplateLicense(Guid workspaceId, Guid templateId, DateTime startDate, DateTime endDate, DateTime now)
    {
        return new License
        {
            WorkspaceId = workspaceId,
            Type = LicenseType.Template,
            Status = DetermineInitialStatus(startDate, endDate, now),
            ValidityPeriod = new DateRange(startDate, endDate),
            TemplateId = templateId
        };
    }

    public static License CreateCustomLicense(Guid workspaceId, DateTime startDate, DateTime endDate, DateTime now)
    {
        return new License
        {
            WorkspaceId = workspaceId,
            Type = LicenseType.Custom,
            Status = DetermineInitialStatus(startDate, endDate, now),
            ValidityPeriod = new DateRange(startDate, endDate),
            TemplateId = null
        };
    }

    private static LicenseStatus DetermineInitialStatus(DateTime start, DateTime end, DateTime now)
    {
        if (now < start) return LicenseStatus.Pending;
        if (now > end) return LicenseStatus.Expired;
        return LicenseStatus.Active;
    }

    public bool IsValidAt(DateTime now)
    {
        return Status == LicenseStatus.Active && ValidityPeriod.IsActive(now);
    }

    public void Extend(DateTime newExpirationDate, DateTime now)
    {
        if (Status is LicenseStatus.Cancelled)
            throw new DomainException("No se puede extender una licencia cancelada.");

        ValidityPeriod = ValidityPeriod.Extend(newExpirationDate);
        Status = DetermineInitialStatus(ValidityPeriod.Start, ValidityPeriod.End, now);
        UpdateTimestamp();
    }

    public void Renew(DateTime newStartDate, DateTime newEndDate, DateTime now)
    {
        if (Status is LicenseStatus.Cancelled)
            throw new DomainException("No se puede renovar una licencia cancelada.");

        ValidityPeriod = ValidityPeriod.Renew(newStartDate, newEndDate);
        Status = DetermineInitialStatus(ValidityPeriod.Start, ValidityPeriod.End, now);
        UpdateTimestamp();
    }

    public void Suspend()
    {
        Status = LicenseStatus.Suspended;
        UpdateTimestamp();
    }

    public void Reactivate(DateTime now)
    {
        if (Status is not LicenseStatus.Suspended)
            throw new DomainException("Solo se pueden reactivar licencias suspendidas.");

        Status = DetermineInitialStatus(ValidityPeriod.Start, ValidityPeriod.End, now);
        UpdateTimestamp();
    }

    public void ChangeTemplate(Guid newTemplateId)
    {
        if (Type != LicenseType.Template)
            throw new DomainException("Solo las licencias de tipo Template pueden cambiar de plantilla.");

        TemplateId = newTemplateId;
        UpdateTimestamp();
    }

    public void AddModule(Guid moduleId)
    {
        if (!_licenseModules.Any(m => m.ModuleId == moduleId))
        {
            _licenseModules.Add(new LicenseModule(Id, moduleId));
            UpdateTimestamp();
        }
    }

    public void RemoveModule(Guid moduleId)
    {
        var module = _licenseModules.FirstOrDefault(m => m.ModuleId == moduleId);
        if (module != null)
        {
            _licenseModules.Remove(module);
            UpdateTimestamp();
        }
    }
}