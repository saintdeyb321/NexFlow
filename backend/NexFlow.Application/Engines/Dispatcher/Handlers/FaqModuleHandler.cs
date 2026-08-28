using NexFlow.Application.Abstractions;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class FaqModuleHandler : IModuleHandler
{
    public string ModuleCode => "FAQ";

    private readonly IFaqRepository _faqRepository;
    private readonly IBusinessProfileRepository _profileRepository;

    public FaqModuleHandler(IFaqRepository faqRepository, IBusinessProfileRepository profileRepository)
    {
        _faqRepository = faqRepository;
        _profileRepository = profileRepository;
    }

    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<string> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode != "READ")
            return "SISTEMA: Capacidad no soportada por el módulo FAQ.";

        var profile = await _profileRepository.GetProfileAsync(workspaceId, cancellationToken);
        var faqs = await _faqRepository.GetFaqsAsync(workspaceId, cancellationToken);

        var relevantFaqs = faqs.Take(10).ToList();

        var faqsText = relevantFaqs.Any()
            ? string.Join(" | ", relevantFaqs.Select(f => $"P: {f.Question} R: {f.Answer}"))
            : "Actualmente no hay preguntas frecuentes configuradas.";

        return profile != null
            ? $"SISTEMA: Responde la duda del cliente basándote en esta información. Nombre negocio: {profile.CommercialName}. FAQs: {faqsText}"
            : $"SISTEMA: Responde la duda del cliente basándote en esta información. FAQs: {faqsText}";
    }
}