using System;

namespace NexFlow.Domain.Entities;

public class LicenseModule
{
    public Guid LicenseId { get; private set; }
    public Guid ModuleId { get; private set; }

    // Constructor privado para Entity Framework
    private LicenseModule() { }

    // Constructor 'internal' para que solo el Dominio pueda instanciarlo
    internal LicenseModule(Guid licenseId, Guid moduleId)
    {
        LicenseId = licenseId;
        ModuleId = moduleId;
    }
}