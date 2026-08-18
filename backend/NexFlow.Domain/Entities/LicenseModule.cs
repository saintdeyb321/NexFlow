namespace NexFlow.Domain.Entities;

// Representa los módulos que tiene una licencia (importante para las Custom)
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

// Representa los módulos que componen una plantilla
public class TemplateModule
{
    public Guid TemplateId { get; private set; }
    public Guid ModuleId { get; private set; }

    private TemplateModule() { }
    public TemplateModule(Guid templateId, Guid moduleId)
    {
        TemplateId = templateId;
        ModuleId = moduleId;
    }
}