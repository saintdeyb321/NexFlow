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
        var activeFaqs = faqs.Where(f => f.IsActive).ToList();

        var searchTerms = string.Join(" ", request.Parameters.Values)
                                .ToLowerInvariant()
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var relevantFaqs = activeFaqs
            .OrderByDescending(f => searchTerms.Count(term => f.Question.ToLowerInvariant().Contains(term) || f.Answer.ToLowerInvariant().Contains(term)))
            .Take(5) // Límite estricto a 5 para no reventar los tokens de Gemini
            .ToList();

        var faqsText = relevantFaqs.Any()
            ? string.Join(" | ", relevantFaqs.Select(f => $"P: {f.Question} R: {f.Answer}"))
            : "Actualmente no hay preguntas frecuentes configuradas o relacionadas.";

        var guardrail = "IMPORTANTE: NUNCA inventes precios ni ofrezcas servicios que no estén explícitamente en estas FAQs. Si el cliente pregunta un precio y no está aquí, dile que vas a revisar el catálogo.";

        var responseText = profile != null
            ? $"Responde la duda del cliente basándote en esta información. Nombre negocio: {profile.CommercialName}. FAQs: {faqsText}. {guardrail}"
            : $"Responde la duda del cliente basándote en esta información. FAQs: {faqsText}. {guardrail}";

        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, responseText, false, Array.Empty<string>());
    }
}