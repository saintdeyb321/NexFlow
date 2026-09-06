using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class FaqModuleHandler : IModuleHandler
{
    public string ModuleCode => "FAQ";

    private readonly IFaqRepository _faqRepository;

    public FaqModuleHandler(IFaqRepository faqRepository)
    {
        _faqRepository = faqRepository;
    }

    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode != "READ")
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "error", message = "Capacidad no soportada" }), false, Array.Empty<string>());

        var faqs = await _faqRepository.GetFaqsAsync(workspaceId, cancellationToken);
        var activeFaqs = faqs.Where(f => f.IsActive).ToList();

        if (!activeFaqs.Any())
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "empty", message = "No hay preguntas frecuentes registradas." }), false, Array.Empty<string>());

        var searchTerms = string.Join(" ", request.Parameters.Values)
                                .ToLowerInvariant()
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Búsqueda inteligente
        var relevantFaqs = activeFaqs
            .Select(f => new
            {
                Faq = f,
                Score = searchTerms.Count(term => f.Question.ToLowerInvariant().Contains(term) || f.Answer.ToLowerInvariant().Contains(term))
            })
            .Where(x => x.Score > 0 || searchTerms.Length == 0) // Si no hay términos de búsqueda, traemos todas (hasta 5)
            .OrderByDescending(x => x.Score)
            .Take(5)
            .Select(x => new { question = x.Faq.Question, answer = x.Faq.Answer })
            .ToList();

        if (!relevantFaqs.Any())
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "not_found", message = "No se encontraron respuestas para esa consulta específica." }), false, Array.Empty<string>());

        // 🔥 SPRINT 9 y 10: Devolvemos JSON puro, sin prompts instruccionales.
        var data = JsonSerializer.Serialize(new { status = "success", results = relevantFaqs });

        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, data, false, Array.Empty<string>());
    }
}