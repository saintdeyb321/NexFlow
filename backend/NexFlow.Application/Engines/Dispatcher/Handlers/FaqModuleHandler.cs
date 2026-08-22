using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    // CORRECCIÓN: La capacidad que este módulo exporta
    public string[] SupportedCapabilities => new[] { "ANSWER_QUESTION" };
    public async Task<string> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        // Pequeño blindaje: validamos que la capacidad sea la correcta
        if (request.CapabilityCode != "ANSWER_QUESTION")
            return "SISTEMA: Capacidad no soportada por el módulo FAQ.";

        var profile = await _profileRepository.GetProfileAsync(workspaceId, cancellationToken);
        var faqs = await _faqRepository.GetFaqsAsync(workspaceId, cancellationToken);

        var faqsText = string.Join(" | ", faqs.Select(f => $"P: {f.Question} R: {f.Answer}"));

        return profile != null
            ? $"SISTEMA: Responde la duda del cliente basándote en esto. Nombre negocio: {profile.CommercialName}. Descripción: {profile.Description}. FAQs: {faqsText}"
            : "SISTEMA: El negocio aún no ha configurado su información básica o preguntas frecuentes.";
    }
}