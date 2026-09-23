using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Common;
using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Features.Business;

public interface ICatalogGenerationService
{
    Task<CatalogArtifact> RequestGenerationAsync(Guid workspaceId, string scope, CancellationToken cancellationToken);

    // 🔥 SPRINT 4: Nuevo método para invalidación automática
    Task CheckAndInvalidateStaleArtifactsAsync(Guid workspaceId, CancellationToken cancellationToken);
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

    // 🔥 SPRINT 4: Lógica de invalidación STALE determinista[cite: 1]
    public async Task CheckAndInvalidateStaleArtifactsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var scopesToVerify = new[] { "PRODUCT", "SERVICE" };

        var allCategories = await _catalogRepository.GetCategoriesAsync(workspaceId, cancellationToken);
        var allItems = await _catalogRepository.GetItemsAsync(workspaceId, cancellationToken);

        foreach (var scope in scopesToVerify)
        {
            var artifact = await _artifactRepository.GetCurrentArtifactAsync(workspaceId, scope, cancellationToken);

            // Solo invalidamos si existe y actualmente se considera vigente (Current)
            if (artifact != null && artifact.Status == CatalogArtifactStatus.Current)
            {
                var filteredCategories = allCategories.Where(c => c.Scope == scope || c.Scope == "SHARED").ToList();
                var filteredItems = allItems.Where(i => i.Type == scope).ToList();

                var currentHash = _hashService.ComputeHash(filteredCategories, filteredItems);

                if (artifact.SourceHash != currentHash)
                {
                    artifact.MarkAsStale();
                    await _artifactRepository.SaveArtifactAsync(artifact, cancellationToken);
                    _logger.LogInformation("El catálogo '{Scope}' del workspace {WorkspaceId} ha mutado. Artefacto invalidado (STALE).", scope, workspaceId);
                }
            }
        }
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

            var rawPayload = new
            {
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
                        i.DurationInMinutes
                    })
                }
            };

            var wrappedPayload = new N8nEventPayload<object>(
                workspaceId,
                "CATALOG_GENERATION_REQUESTED",
                Guid.NewGuid().ToString(),
                $"catalog_{generationId}",
                DateTime.UtcNow,
                rawPayload);

            _ = Task.Run(async () =>
            {
                try
                {
                    await _workflowGateway.TriggerWorkflowAsync("nexflow-events", wrappedPayload, CancellationToken.None);
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