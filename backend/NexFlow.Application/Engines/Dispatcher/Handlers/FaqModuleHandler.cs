using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;

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

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode != "READ")
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "Capacidad no soportada por el módulo FAQ.", false, Array.Empty<string>());

        var profile = await _profileRepository.GetProfileAsync(workspaceId, cancellationToken);
        var faqs = await _faqRepository.GetFaqsAsync(workspaceId, cancellationToken);

        var relevantFaqs = faqs.Take(10).ToList();

        var faqsText = relevantFaqs.Any()
            ? string.Join(" | ", relevantFaqs.Select(f => $"P: {f.Question} R: {f.Answer}"))
            : "Actualmente no hay preguntas frecuentes configuradas.";

        var responseText = profile != null
            ? $"Responde la duda del cliente basándote en esta información. Nombre negocio: {profile.CommercialName}. FAQs: {faqsText}"
            : $"Responde la duda del cliente basándote en esta información. FAQs: {faqsText}";

        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, responseText, false, Array.Empty<string>());
    }
}