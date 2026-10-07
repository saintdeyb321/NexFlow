using Microsoft.Extensions.Logging;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Integrations;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Features.Services.DTOs;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.Exceptions; // Agregado para usar excepciones limpias
using NexFlow.Domain.ValueObjects;
using System.Text.Json;

namespace NexFlow.Application.Features.Business;

public interface ICatalogGenerationService
{
    Task<CatalogArtifact?> GetArtifactAsync(Guid workspaceId, string scope, CancellationToken cancellationToken);
    Task<string> GetCurrentSourceHashAsync(Guid workspaceId, string scope, CancellationToken cancellationToken);
    Task<CatalogArtifact> RequestGenerationAsync(Guid workspaceId, string scope, ArtifactDesign design, bool replaceCurrent, CancellationToken cancellationToken);
    Task<CatalogArtifact> UploadPdfAsync(Guid workspaceId, string scope, Stream fileStream, string fileName, string contentType, long length, bool replaceCurrent, CancellationToken cancellationToken);
    Task CheckAndInvalidateStaleArtifactsAsync(Guid workspaceId, CancellationToken cancellationToken);
}

public class CatalogGenerationService : ICatalogGenerationService
{
    public const long MaxPdfBytes = 15 * 1024 * 1024;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogArtifactRepository _artifactRepository;
    private readonly ICatalogGenerationUsageRepository _usageRepository;
    private readonly ICatalogHashService _hashService;
    private readonly IBusinessProfileRepository _profileRepository;
    private readonly ILocationRepository _locationRepository;
    private readonly IFileStorage _fileStorage;
    private readonly IOutboxRepository _outboxRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<CatalogGenerationService> _logger;

    public CatalogGenerationService(
        ICatalogRepository catalogRepository,
        ICatalogArtifactRepository artifactRepository,
        ICatalogGenerationUsageRepository usageRepository,
        ICatalogHashService hashService,
        IBusinessProfileRepository profileRepository,
        ILocationRepository locationRepository,
        IFileStorage fileStorage,
        IOutboxRepository outboxRepository,
        IUnitOfWork unitOfWork,
        ILogger<CatalogGenerationService> logger)
    {
        _catalogRepository = catalogRepository;
        _artifactRepository = artifactRepository;
        _usageRepository = usageRepository;
        _hashService = hashService;
        _profileRepository = profileRepository;
        _locationRepository = locationRepository;
        _fileStorage = fileStorage;
        _outboxRepository = outboxRepository;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    private static string ValidateScope(string scope)
    {
        var value = scope?.Trim().ToUpperInvariant();
        if (value is not ("PRODUCT" or "SERVICE")) throw new ArgumentException("Scope debe ser PRODUCT o SERVICE.");
        return value;
    }

    private async Task<(object BusinessData, object CatalogData, object Locations)> GetContentAsync(Guid workspaceId, string scope, CancellationToken ct)
    {
        var profile = await _profileRepository.GetProfileAsync(workspaceId, ct)
            ?? throw new KeyNotFoundException("Perfil comercial no encontrado.");
        var categories = (await _catalogRepository.GetActiveCategoriesAsync(workspaceId, ct))
            .Where(c => c.Scope == scope || c.Scope == "SHARED").OrderBy(c => c.DisplayOrder).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        var categoryIds = categories.Select(c => c.Id).ToHashSet();
        var items = (await _catalogRepository.GetItemsByTypeAsync(workspaceId, scope, ct))
            .Where(i => i.IsActive && (string.IsNullOrEmpty(i.CategoryId) || categoryIds.Contains(i.CategoryId)))
            .OrderBy(i => i.Id, StringComparer.Ordinal).ToList();
        var locations = (await _locationRepository.GetLocationsAsync(workspaceId, ct))
            .OrderBy(l => l.Id, StringComparer.Ordinal)
            .Select(l => new { l.Id, l.Name, l.Address, l.Reference, l.MapUrl, l.IsMain }).ToArray();
        return (new { Name = profile.CommercialName, Email = profile.ContactEmail, WhatsApp = profile.WhatsAppNumber, profile.Description, profile.TaxId, profile.TimeZone },
            new {
                Categories = categories.Select(c => new { c.Id, c.Name, c.Description, c.DisplayOrder, c.Scope }).ToArray(),
                Items = items.Select(i => new { i.Id, i.CategoryId, i.Name, i.Description, Price = i.PriceMinorUnits / 100m,
                    i.Currency, i.ImageUrl, i.LocationScope, LocationIds = i.LocationIds.OrderBy(id => id, StringComparer.Ordinal).ToArray(),
                    DurationInMinutes = (i as ServiceDto)?.DurationInMinutes, RequiresReservation = (i as ServiceDto)?.RequiresReservation }).ToArray()
            }, locations);
    }

    public async Task<string> GetCurrentSourceHashAsync(Guid workspaceId, string scope, CancellationToken ct)
    {
        var content = await GetContentAsync(workspaceId, ValidateScope(scope), ct);
        return _hashService.ComputeContentHash(new { content.BusinessData, content.CatalogData, content.Locations });
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

    public async Task<CatalogArtifact> RequestGenerationAsync(Guid workspaceId, string scope, ArtifactDesign design, bool replaceCurrent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(design);
        scope = ValidateScope(scope);
        var artifact = await GetArtifactAsync(workspaceId, scope, ct) ?? CatalogArtifact.Initialize(workspaceId, scope);
        artifact.EnsureCanReplace(replaceCurrent);
        var content = await GetContentAsync(workspaceId, scope, ct);
        var hash = _hashService.ComputeContentHash(new { content.BusinessData, content.CatalogData, content.Locations });
        await _usageRepository.IncrementUsageAtomicallyAsync(workspaceId, DateTime.UtcNow.Date, ct);
        var generationId = Guid.NewGuid().ToString("N");
        artifact.MarkAsGenerating(hash, generationId, design);
        try
        {
            await _artifactRepository.SaveArtifactAsync(artifact, ct);
            var payload = new N8nEventPayload<object>(workspaceId, "CATALOG_GENERATION_REQUESTED", generationId,
                $"catalog_{generationId}", DateTime.UtcNow,
                new { GenerationId = generationId, Scope = scope, SourceHash = hash,
                    Design = new { VisualStyle = design.VisualStyle.ToString(), Palette = design.Palette.ToString(), Creativity = design.Creativity.ToString() },
                    content.BusinessData, content.CatalogData, content.Locations });
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

    public async Task<CatalogArtifact> UploadPdfAsync(Guid workspaceId, string scope, Stream fileStream, string fileName,
        string contentType, long length, bool replaceCurrent, CancellationToken ct)
    {
        scope = ValidateScope(scope);
        if (length < 5 || length > MaxPdfBytes) throw new DomainException("El PDF debe pesar como máximo 15 MB y no estar vacío.");
        if (!string.Equals(contentType, "application/pdf", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(fileName), ".pdf", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Solo se permiten archivos .pdf con Content-Type application/pdf.");
        if (!fileStream.CanSeek || fileStream.Length != length) throw new DomainException("No se pudo leer el archivo PDF.");

        fileStream.Position = 0;
        var header = new byte[5];
        var bytesRead = await fileStream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken: ct);
        if (bytesRead != header.Length || !header.AsSpan().SequenceEqual("%PDF-"u8))
            throw new DomainException("El archivo no tiene una cabecera PDF válida.");
        fileStream.Position = 0;

        var artifact = await GetArtifactAsync(workspaceId, scope, ct) ?? CatalogArtifact.Initialize(workspaceId, scope);
        artifact.EnsureCanReplace(replaceCurrent);
        var safeFileName = $"{Guid.NewGuid():N}.pdf";
        var folder = $"nexflow/workspaces/{workspaceId:D}/artifacts/{scope}";
        var pdfUrl = await _fileStorage.UploadPdfAsync(fileStream, safeFileName, folder, ct);
        var hash = await GetCurrentSourceHashAsync(workspaceId, scope, ct);
        artifact.CompleteUpload(pdfUrl, hash);
        // The same Firestore version check prevents an upload from overwriting a concurrent generation.
        await _artifactRepository.SaveArtifactAsync(artifact, ct);
        return artifact;
    }
}
