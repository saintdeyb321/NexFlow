using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Exceptions; // Agregado para usar excepciones limpias
using System.Text.Json;

namespace NexFlow.Application.Features.Business;

public interface ICatalogGenerationService
{
    Task<string> GetCurrentSourceHashAsync(Guid workspaceId, string scope, CancellationToken cancellationToken);
    Task<CatalogArtifact> RequestGenerationAsync(Guid workspaceId, string scope, CancellationToken cancellationToken);
    Task CheckAndInvalidateStaleArtifactsAsync(Guid workspaceId, CancellationToken cancellationToken);
}

public class CatalogGenerationService : ICatalogGenerationService
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogArtifactRepository _artifactRepository;
    private readonly ICatalogGenerationUsageRepository _usageRepository;
    private readonly ICatalogHashService _hashService;
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork; // 🔥 SPRINT 11: Requerido para guardar el Outbox
    private readonly ILogger<CatalogGenerationService> _logger;

    public CatalogGenerationService(
        ICatalogRepository catalogRepository,
        ICatalogArtifactRepository artifactRepository,
        ICatalogGenerationUsageRepository usageRepository,
        ICatalogHashService hashService,
        IBusinessProfileRepository profileRepository,
        IOutboxRepository outboxRepository,
        IUnitOfWork unitOfWork,
        ILogger<CatalogGenerationService> logger)
    {
        _catalogRepository = catalogRepository;
        _artifactRepository = artifactRepository;
        _usageRepository = usageRepository;
        _hashService = hashService;
        _profileRepository = profileRepository;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<string> GetCurrentSourceHashAsync(Guid workspaceId, string scope, CancellationToken cancellationToken)
    {
        var targetScope = scope.Trim().ToUpperInvariant();
        if (targetScope != "PRODUCT" && targetScope != "SERVICE")
            throw new ArgumentException("El scope debe ser PRODUCT o SERVICE.", nameof(scope));

        var categories = await _catalogRepository.GetCategoriesAsync(workspaceId, cancellationToken);
        var items = await _catalogRepository.GetItemsAsync(workspaceId, cancellationToken);
        return _hashService.ComputeHash(
            categories.Where(c => c.Scope == targetScope || c.Scope == "SHARED").ToList(),
            items.Where(i => i.Type == targetScope).ToList());
    }

    public async Task CheckAndInvalidateStaleArtifactsAsync(Guid workspaceId, CancellationToken cancellationToken)
    {
        var scopesToVerify = new[] { "PRODUCT", "SERVICE" };
        var allCategories = await _catalogRepository.GetCategoriesAsync(workspaceId, cancellationToken);
        var allItems = await _catalogRepository.GetItemsAsync(workspaceId, cancellationToken);

        foreach (var scope in scopesToVerify)
        {
            var artifact = await _artifactRepository.GetCurrentArtifactAsync(workspaceId, scope, cancellationToken);

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

        if (targetScope != "PRODUCT" && targetScope != "SERVICE")
        {
            throw new ArgumentException("El scope del artefacto debe ser estrictamente PRODUCT o SERVICE.", nameof(scope));
        }

        var allCategories = await _catalogRepository.GetCategoriesAsync(workspaceId, cancellationToken);
        var allItems = await _catalogRepository.GetItemsAsync(workspaceId, cancellationToken);

        var filteredCategories = allCategories.Where(c => c.Scope == targetScope || c.Scope == "SHARED").ToList();
        var filteredItems = allItems.Where(i => i.Type == targetScope).ToList();

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
                        DurationInMinutes = (i as ServiceDto)?.DurationInMinutes
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

            var outboxMessage = new OutboxMessage
            {
                WorkspaceId = workspaceId,
                EventType = "CATALOG_GENERATION_REQUESTED",
                PayloadJson = JsonSerializer.Serialize(wrappedPayload)
            };

            await _outboxRepository.AddAsync(outboxMessage, cancellationToken);

            // 🔥 SPRINT 11 (Auditoría): Guardamos el evento en la BD. Si no, nunca será procesado por el Worker.
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error preparando el payload documental para n8n o guardando en Outbox");
            throw; // Propagamos. No tocamos el estado interno con reflexión para no ensuciar el Dominio.
        }

        return artifact;
    }
}