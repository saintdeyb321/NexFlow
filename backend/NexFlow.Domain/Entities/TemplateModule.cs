namespace NexFlow.Domain.Entities;

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