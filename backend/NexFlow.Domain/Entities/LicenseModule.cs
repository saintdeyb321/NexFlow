namespace NexFlow.Domain.Entities;

public class LicenseModule
{
    public Guid LicenseId { get; private set; }
    public Guid ModuleId { get; private set; }

    private LicenseModule() { }

    public LicenseModule(Guid licenseId, Guid moduleId)
    {
        LicenseId = licenseId;
        ModuleId = moduleId;
    }
}