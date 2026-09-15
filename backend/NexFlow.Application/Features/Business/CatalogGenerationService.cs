using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Features.Business;

public interface ICatalogGenerationService
{
    Task<CatalogArtifact> RequestGenerationAsync(Guid workspaceId, string scope, CancellationToken cancellationToken);
}

public class CatalogGenerationService : ICatalogGenerationService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogArtifactRepository _artifactRepository;
    private readonly ICatalogGenerationUsageRepository _usageRepository;
    private readonly ICatalogHashService _hashService;
    private readonly IWorkflowGateway _workflowGateway;
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly ILogger<CatalogGenerationService> _logger;

    public CatalogGenerationService(
        ICatalogRepository catalogRepository,
        ICatalogArtifactRepository artifactRepository,
        ICatalogGenerationUsageRepository usageRepository,
        ICatalogHashService hashService,
        IWorkflowGateway workflowGateway,
        IBusinessProfileRepository profileRepository,
        ILogger<CatalogGenerationService> logger)
    {
        _catalogRepository = catalogRepository;
        _artifactRepository = artifactRepository;
        _usageRepository = usageRepository;
        _hashService = hashService;
        _workflowGateway = workflowGateway;
        _profileRepository = profileRepository;
        _logger = logger;
    }

    public async Task<CatalogArtifact> RequestGenerationAsync(Guid workspaceId, string scope, CancellationToken cancellationToken)
    {
        var targetScope = scope.ToUpperInvariant();

        var allCategories = await _catalogRepository.GetCategoriesAsync(workspaceId, cancellationToken);
        var allItems = await _catalogRepository.GetItemsAsync(workspaceId, cancellationToken);

        var filteredCategories = targetScope == "COMBINED" ? allCategories : allCategories.Where(c => c.Scope == targetScope || c.Scope == "SHARED").ToList();
        var filteredItems = targetScope == "COMBINED" ? allItems : allItems.Where(i => i.Type == targetScope).ToList();

        var currentHash = _hashService.ComputeHash(filteredCategories, filteredItems);

        var artifact = await _artifactRepository.GetCurrentArtifactAsync(workspaceId, targetScope, cancellationToken)
                       ?? CatalogArtifact.Initialize(workspaceId, targetScope);

        if (artifact.Status == CatalogArtifactStatus.Current && artifact.SourceHash == currentHash)
        {
            return artifact;
        }

        await _usageRepository.IncrementUsageAtomicallyAsync(workspaceId, DateTime.UtcNow.Date, cancellationToken);

        string generationId = Guid.NewGuid().ToString("N");
        artifact.MarkAsGenerating(currentHash, generationId);
        await _artifactRepository.SaveArtifactAsync(artifact, cancellationToken);

        try
        {
            var profile = await _profileRepository.GetProfileAsync(workspaceId, cancellationToken);

            var payload = new
            {
                WorkspaceId = workspaceId,
                GenerationId = generationId,
                Scope = targetScope,
                SourceHash = currentHash,
                BusinessData = new
                {
                    Name = profile?.CommercialName ?? "Negocio sin nombre",
                    Email = profile?.ContactEmail,
                    WhatsApp = profile?.WhatsAppNumber
                },
                CatalogData = new
                {
                    Categories = filteredCategories.Select(c => new { c.Id, c.Name, c.Description }),
                    Items = filteredItems.Select(i => new {
                        i.Id,
                        i.CategoryId,
                        i.Name,
                        i.Description,
                        Price = i.PriceMinorUnits / 100m,
                        i.Currency,
                        i.ImageUrl,
                        i.DurationInMinutes,
                        i.LocationScope, // 🔥 SPRINT 15: n8n debe conocer las sedes para pintarlas en el PDF
                        i.LocationIds
                    })
                }
            };

            string jsonPayload = JsonSerializer.Serialize(payload);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _workflowGateway.TriggerCatalogGenerationAsync(jsonPayload, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Fallo al enviar el trigger a n8n para el workspace {WorkspaceId}", workspaceId);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preparando el payload para n8n");
        }

        return artifact;
    }
}