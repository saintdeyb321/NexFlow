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
    Task<CatalogArtifact?> GetArtifactAsync(Guid workspaceId, string scope, CancellationToken cancellationToken);
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
    private readonly IUnitOfWork _unitOfWork;
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

    private static string ValidateScope(string scope)
    {
        var value = scope.Trim().ToUpperInvariant();
        if (value is not ("PRODUCT" or "SERVICE")) throw new ArgumentException("Scope debe ser PRODUCT o SERVICE.");
        return value;
    }

    private async Task<(object BusinessData, object CatalogData)> GetContentAsync(Guid workspaceId, string scope, CancellationToken ct)
    {
        var profile = await _profileRepository.GetProfileAsync(workspaceId, ct)
            ?? throw new KeyNotFoundException("Perfil comercial no encontrado.");
        var categories = (await _catalogRepository.GetActiveCategoriesAsync(workspaceId, ct))
            .Where(c => c.Scope == scope || c.Scope == "SHARED").OrderBy(c => c.DisplayOrder).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var categoryIds = categories.Select(c => c.Id).ToHashSet();
        var items = (await _catalogRepository.GetItemsByTypeAsync(workspaceId, scope, ct))
            .Where(i => i.IsActive && (string.IsNullOrEmpty(i.CategoryId) || categoryIds.Contains(i.CategoryId)))
            .OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        return (new { Name = profile.CommercialName, Email = profile.ContactEmail, WhatsApp = profile.WhatsAppNumber, profile.Description, profile.TaxId, profile.TimeZone },
            new {
                Categories = categories.Select(c => new { c.Id, c.Name, c.Description, c.DisplayOrder, c.Scope }).ToArray(),
                Items = items.Select(i => new { i.Id, i.CategoryId, i.Name, i.Description, Price = i.PriceMinorUnits / 100m,
                    i.Currency, i.ImageUrl, i.LocationScope, LocationIds = i.LocationIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    DurationInMinutes = (i as ServiceDto)?.DurationInMinutes, RequiresReservation = (i as ServiceDto)?.RequiresReservation }).ToArray()
            });
    }

    public async Task<string> GetCurrentSourceHashAsync(Guid workspaceId, string scope, CancellationToken ct)
    {
        var content = await GetContentAsync(workspaceId, ValidateScope(scope), ct);
        return _hashService.ComputeContentHash(new { content.BusinessData, content.CatalogData });
    }

    public async Task<CatalogArtifact?> GetArtifactAsync(Guid workspaceId, string scope, CancellationToken ct)
    {
        scope = ValidateScope(scope);
        var artifact = await _artifactRepository.GetCurrentArtifactAsync(workspaceId, scope, ct);
        if (artifact == null) return null;
        if (artifact.Status == CatalogArtifactStatus.Generating &&
            (artifact.GenerationStartedAt == null || artifact.GenerationStartedAt < DateTime.UtcNow.AddHours(-1)))
        {
            artifact.MarkAsFailed();
            await _artifactRepository.SaveArtifactAsync(artifact, ct);
        }
        if (artifact.Status == CatalogArtifactStatus.Current && artifact.SourceHash != await GetCurrentSourceHashAsync(workspaceId, scope, ct))
        {
            artifact.MarkAsStale();
            await _artifactRepository.SaveArtifactAsync(artifact, ct);
        }
        return artifact;
    }

    public async Task CheckAndInvalidateStaleArtifactsAsync(Guid workspaceId, CancellationToken ct)
    {
        foreach (var scope in new[] { "PRODUCT", "SERVICE" }) await GetArtifactAsync(workspaceId, scope, ct);
    }

    public async Task<CatalogArtifact> RequestGenerationAsync(Guid workspaceId, string scope, CancellationToken ct)
    {
        scope = ValidateScope(scope);
        var content = await GetContentAsync(workspaceId, scope, ct);
        var hash = _hashService.ComputeContentHash(new { content.BusinessData, content.CatalogData });
        var artifact = await GetArtifactAsync(workspaceId, scope, ct) ?? CatalogArtifact.Initialize(workspaceId, scope);
        if (artifact.SourceHash == hash && artifact.Status is CatalogArtifactStatus.Current or CatalogArtifactStatus.Generating) return artifact;
        await _usageRepository.IncrementUsageAtomicallyAsync(workspaceId, DateTime.UtcNow.Date, ct);
        var generationId = Guid.NewGuid().ToString("N");
        artifact.MarkAsGenerating(hash, generationId);
        try
        {
            await _artifactRepository.SaveArtifactAsync(artifact, ct);
            var payload = new N8nEventPayload<object>(workspaceId, "CATALOG_GENERATION_REQUESTED", generationId,
                $"catalog_{generationId}", DateTime.UtcNow,
                new { GenerationId = generationId, Scope = scope, SourceHash = hash, content.BusinessData, content.CatalogData });
            await _outboxRepository.AddAsync(new OutboxMessage { WorkspaceId = workspaceId, EventType = payload.EventType, PayloadJson = JsonSerializer.Serialize(payload) }, ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Catalog generation enqueue failed for {WorkspaceId}/{GenerationId}", workspaceId, generationId);
            artifact.MarkAsFailed();
            // A request cancellation must not prevent the durable failure marker.
            using var recovery = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try { await _artifactRepository.SaveArtifactAsync(artifact, recovery.Token); }
            catch (Exception failure) { _logger.LogError(failure, "Failed to persist artifact failure; read reconciliation will recover {GenerationId}", generationId); }
            throw;
        }
        return artifact;
    }
}
