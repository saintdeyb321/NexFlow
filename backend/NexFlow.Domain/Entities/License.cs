using System;
using System.Collections.Generic;
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

    // Constructor privado estricto para EF Core
    private License() { }

    public static License CreateTemplateLicense(Guid workspaceId, Guid templateId, DateTime now, DateTime expiresAt)
    {
        // 1. Invariante: La expiración debe tener sentido temporal
        if (expiresAt <= now)
        {
            throw new DomainException("La fecha de expiración debe ser mayor a la fecha de inicio.");
        }

        // 2. Invariante: Una licencia de plantilla DEBE tener un TemplateId
        if (templateId == Guid.Empty)
        {
            throw new DomainException("Una licencia basada en plantilla requiere un ID de plantilla válido.");
        }

        return new License
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Type = LicenseType.Template,
            TemplateId = templateId,
            Status = LicenseStatus.Active, // Estado administrativo
            ValidityPeriod = new DateRange(now, expiresAt)
        };
    }

    public void AddModule(Guid moduleId)
    {
        if (Status != LicenseStatus.Active)
        {
            throw new DomainException("No se pueden agregar módulos a una licencia que no está activa.");
        }

        // Usamos el constructor internal en lugar de los inicializadores
        _licenseModules.Add(new LicenseModule(this.Id, moduleId));
    }

    // 3. Método de Validación Dinámica (Requerido por la auditoría)
    // La autorización debe llamar a este método, no leer el "Status" directamente.
    public bool IsValidAt(DateTime currentDate)
    {
        return Status == LicenseStatus.Active &&
               currentDate >= ValidityPeriod.Start &&
               currentDate <= ValidityPeriod.End;
    }

    public void Suspend()
    {
        Status = LicenseStatus.Suspended;
    }

    public void Renew(DateTime newStartDate, DateTime newEndDate, DateTime currentDate)
    {
        if (newEndDate <= newStartDate)
        {
            throw new DomainException("La nueva fecha de fin debe ser mayor a la fecha de inicio.");
        }

        ValidityPeriod = new DateRange(newStartDate, newEndDate);
        Status = LicenseStatus.Active;
    }
}