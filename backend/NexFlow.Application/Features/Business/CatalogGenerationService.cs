using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Features.Business;

public interface ICatalogGenerationService
{
    Task<CatalogArtifact> RequestGenerationAsync(Guid workspaceId, CancellationToken cancellationToken);
}

public class CatalogGenerationService : ICatalogGenerationService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogArtifactRepository _artifactRepository;
    private readonly ICatalogGenerationUsageRepository _usageRepository;
    private readonly ICatalogHashService _hashService;

    public CatalogGenerationService(
        ICatalogRepository catalogRepository,
        ICatalogArtifactRepository artifactRepository,
        ICatalogGenerationUsageRepository usageRepository,
        ICatalogHashService hashService)
    {
        _catalogRepository = catalogRepository;
        _artifactRepository = artifactRepository;
        _usageRepository = usageRepository;
        _hashService = hashService;
    }

    public async Task<CatalogArtifact> RequestGenerationAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        // 1. Obtenemos categorías e ítems actuales para calcular su firma (Hash)
        var categories = await _catalogRepository.GetCategoriesAsync(workspaceId, cancellationToken);
        var items = await _catalogRepository.GetItemsAsync(workspaceId, cancellationToken);

        var currentHash = _hashService.ComputeHash(categories, items);

        // 2. Verificamos el artefacto actual existente en Firestore
        var artifact = await _artifactRepository.GetCurrentArtifactAsync(workspaceId, cancellationToken)
                       ?? CatalogArtifact.Initialize(workspaceId);

        // 3. Si el hash coincide y el estado es Current, no gastamos IA ni recursos
        if (artifact.Status == CatalogArtifactStatus.Current && artifact.SourceHash == currentHash)
        {
            return artifact; // El PDF existente sigue siendo totalmente válido
        }

        // 4. Validamos el límite diario (Máximo 3 por día)
        var today = DateTime.UtcNow.Date;
        var usage = await _usageRepository.GetUsageForTodayAsync(workspaceId, today, cancellationToken)
                    ?? CatalogGenerationUsage.Create(workspaceId, today);

        usage.Increment(); // Dispara DomainException si supera 3

        // 5. Marcamos el artefacto como en proceso con su nuevo hash
        artifact.MarkAsGenerating(currentHash);

        // 6. Guardamos los cambios de uso y estado en Firestore
        await _usageRepository.SaveUsageAsync(usage, cancellationToken);
        await _artifactRepository.SaveArtifactAsync(artifact, cancellationToken);

        // TODO (Sprint 8): Aquí inyectaremos el llamado a n8n para que renderice el PDF en segundo plano.

        return artifact;
    }
}