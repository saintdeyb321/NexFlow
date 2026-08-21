using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Intent.AI;

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

    public bool CanHandle(IntentType intent) => intent == IntentType.Faq;

    public async Task<string> ExecuteCapabilityAsync(Guid workspaceId, IntentResultDto intent, CancellationToken cancellationToken)
    {
        var profile = await _profileRepository.GetProfileAsync(workspaceId, cancellationToken);
        var faqs = await _faqRepository.GetFaqsAsync(workspaceId, cancellationToken);

        var faqsText = string.Join(" | ", faqs.Select(f => $"P: {f.Question} R: {f.Answer}"));

        return profile != null
            ? $"Nombre negocio: {profile.CommercialName}. Descripción: {profile.Description}. FAQs: {faqsText}"
            : "Contexto genérico.";
    }
}