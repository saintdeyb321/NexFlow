using Microsoft.EntityFrameworkCore;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Exceptions;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.API.Services.BackgroundServices;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/catalog")]
[Authorize(Policy = "WorkspaceMember")]
public class CatalogController : ControllerBase
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;
    private readonly IBackgroundTaskQueue _taskQueue;

    public CatalogController(
        ICatalogRepository catalogRepository,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService,
        IBackgroundTaskQueue taskQueue)
    {
        _catalogRepository = catalogRepository;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
        _taskQueue = taskQueue;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    private async Task<bool> HasAccessTo(string moduleCode, CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains(moduleCode.ToUpperInvariant());
    }

    private async Task<bool> HasAccessToCategoryScope(string? scope, bool write, CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        var modules = activeModules.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return scope switch
        {
            "PRODUCT" => modules.Contains("CATALOG"),
            "SERVICE" => modules.Contains("SERVICES"),
            "SHARED" => write
                ? modules.Contains("CATALOG") && modules.Contains("SERVICES")
                : modules.Contains("CATALOG") || modules.Contains("SERVICES"),
            _ => false
        };
    }

    // ==========================================
    // CATEGORÍAS
    // ==========================================
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories([FromQuery] string? scope, CancellationToken cancellationToken)
    {
        var targetScope = string.IsNullOrWhiteSpace(scope) ? null : scope.Trim().ToUpperInvariant();
        if (targetScope != null && targetScope != "PRODUCT" && targetScope != "SERVICE" && targetScope != "SHARED")
            return BadRequest(new { message = "Scope inválido." });

        var modules = (await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, cancellationToken))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var canReadProducts = modules.Contains("CATALOG");
        var canReadServices = modules.Contains("SERVICES");
        if ((!canReadProducts && !canReadServices)
            || (targetScope == "PRODUCT" && !canReadProducts)
            || (targetScope == "SERVICE" && !canReadServices))
            return StatusCode(403, "Acceso denegado al scope solicitado.");

        var categories = await _catalogRepository.GetCategoriesAsync(WorkspaceId, cancellationToken);
        return Ok(categories.Where(c =>
                (c.Scope == "PRODUCT" && canReadProducts)
                || (c.Scope == "SERVICE" && canReadServices)
                || c.Scope == "SHARED")
            .Where(c => targetScope == null || c.Scope == targetScope || c.Scope == "SHARED")
            .ToList());
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] ProductCategoryDto category, CancellationToken cancellationToken)
    {
        category.Scope = category.Scope?.Trim().ToUpperInvariant() ?? string.Empty;
        if (category.Scope != "PRODUCT" && category.Scope != "SERVICE" && category.Scope != "SHARED")
            return BadRequest(new { message = "Scope inválido." });
        if (!await HasAccessToCategoryScope(category.Scope, true, cancellationToken))
            return StatusCode(403, "Acceso denegado al scope de la categoría.");

        if (string.IsNullOrWhiteSpace(category.Id)) category.Id = Guid.NewGuid().ToString();
        else
        {
            var existing = await _catalogRepository.GetCategoryByIdAsync(WorkspaceId, category.Id, cancellationToken);
            if (existing != null)
            {
                if (!await HasAccessToCategoryScope(existing.Scope, true, cancellationToken))
                    return StatusCode(403, "Acceso denegado a la categoría existente.");
                if (existing.Scope != category.Scope)
                    return BadRequest(new { message = "No se permite cambiar el scope de una categoría existente." });
            }
        }
        try { await _catalogRepository.SaveCategoryAsync(WorkspaceId, category, cancellationToken); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
        QueueArtifactInvalidation(WorkspaceId);

        return Ok(category);
    }

    [HttpPut("categories/{categoryId}")]
    public async Task<IActionResult> UpdateCategory(string categoryId, [FromBody] ProductCategoryDto category, CancellationToken cancellationToken)
    {
        category.Scope = category.Scope?.Trim().ToUpperInvariant() ?? string.Empty;
        if (category.Scope != "PRODUCT" && category.Scope != "SERVICE" && category.Scope != "SHARED")
            return BadRequest(new { message = "Scope inválido." });
        if (!await HasAccessToCategoryScope(category.Scope, true, cancellationToken))
            return StatusCode(403, "Acceso denegado al scope de la categoría.");
        var existing = await _catalogRepository.GetCategoryByIdAsync(WorkspaceId, categoryId, cancellationToken);
        if (existing == null) return NotFound();
        if (!await HasAccessToCategoryScope(existing.Scope, true, cancellationToken))
            return StatusCode(403, "Acceso denegado a la categoría existente.");
        if (existing.Scope != category.Scope)
            return BadRequest(new { message = "No se permite cambiar el scope de una categoría existente." });

        category.Id = categoryId;
        try { await _catalogRepository.SaveCategoryAsync(WorkspaceId, category, cancellationToken); }
        catch (DomainException ex) { return BadRequest(new { message = ex.Message }); }
        QueueArtifactInvalidation(WorkspaceId);

        return Ok(category);
    }

    [HttpDelete("categories/{categoryId}")]
    public async Task<IActionResult> DeleteCategory(string categoryId, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategoryScope("SHARED", false, cancellationToken))
            return StatusCode(403, "Acceso denegado.");
        var category = await _catalogRepository.GetCategoryByIdAsync(WorkspaceId, categoryId, cancellationToken);
        if (category == null) return NotFound();
        if (!await HasAccessToCategoryScope(category.Scope, true, cancellationToken))
            return StatusCode(403, "Acceso denegado al scope de la categoría.");

        var items = await _catalogRepository.GetItemsByCategoryAsync(WorkspaceId, categoryId, cancellationToken);
        if (items.Any()) return BadRequest(new { message = "No puedes eliminar una categoría que contiene productos o servicios." });

        try { await _catalogRepository.DeleteCategoryAsync(WorkspaceId, categoryId, cancellationToken); }
        catch (DomainException ex) { return Conflict(new { message = ex.Message }); }
        QueueArtifactInvalidation(WorkspaceId);

        return NoContent();
    }

    // =======================================================
    // PRODUCTS
    // =======================================================
    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] string? locationId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        var allItems = await _catalogRepository.GetItemsByTypeAsync(WorkspaceId, "PRODUCT", cancellationToken);
        var products = allItems.Where(i => i.Type == "PRODUCT");

        if (!string.IsNullOrWhiteSpace(locationId))
        {
            products = products.Where(p =>
                string.Equals(p.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase) ||
                (p.LocationIds != null && p.LocationIds.Contains(locationId))
            );
        }

        return Ok(products.ToList());
    }

    [HttpPost]
    public async Task<IActionResult> SaveProduct([FromBody] ProductDto product, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        if (product.Type != "PRODUCT") return BadRequest(new { message = "Tipo de offering inválido." });
        if (string.IsNullOrWhiteSpace(product.Id)) product.Id = Guid.NewGuid().ToString();
        try { await _catalogRepository.SaveItemAsync(WorkspaceId, product, cancellationToken); }
        catch (NexFlow.Domain.Exceptions.DomainException ex) { return BadRequest(new { message = ex.Message }); }
        QueueArtifactInvalidation(WorkspaceId);

        return Ok(product);
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> DeleteProduct(string productId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        var item = await _catalogRepository.GetItemByIdAsync(WorkspaceId, productId, cancellationToken);
        if (item != null && item.Type != "PRODUCT")
            return StatusCode(403, "Este endpoint solo permite eliminar productos.");
        if (item != null)
        {
            await _catalogRepository.DeleteItemAsync(WorkspaceId, productId, cancellationToken);
            QueueArtifactInvalidation(WorkspaceId);
        }

        return NoContent();
    }

    // ==========================================
    // ARTEFACTOS Y PDF
    // ==========================================
    [HttpGet("artifact")]
    public async Task<IActionResult> GetArtifactStatus(
        [FromQuery] string? scope,
        [FromServices] ICatalogGenerationService generationService,
        CancellationToken cancellationToken)
    {
        var targetScope = string.IsNullOrWhiteSpace(scope) ? "PRODUCT" : scope.Trim().ToUpperInvariant();
        if (targetScope != "PRODUCT" && targetScope != "SERVICE")
            return BadRequest(new { message = "Scope inválido. Usa PRODUCT o SERVICE." });

        var requiredModule = targetScope == "SERVICE" ? "SERVICES" : "CATALOG";

        if (!await HasAccessTo(requiredModule, cancellationToken))
            return StatusCode(403, $"Módulo {requiredModule} no contratado.");

        var artifact = await generationService.GetArtifactAsync(WorkspaceId, targetScope, cancellationToken);

        if (artifact == null)
            return Ok(new { status = "NOT_GENERATED", pdfUrl = (string?)null });
        return Ok(new
        {
            status = artifact.Status.ToString().ToUpperInvariant(),
            pdfUrl = artifact.Status == CatalogArtifactStatus.Current ? artifact.PdfUrl : null,
            lastGeneratedAt = artifact.LastGeneratedAt
        });
    }

    [HttpPost("artifact/generate")]
    public async Task<IActionResult> GenerateArtifact(
        [FromBody] GenerateArtifactRequest request,
        [FromServices] ICatalogGenerationService generationService,
        CancellationToken cancellationToken)
    {
        var targetScope = string.IsNullOrWhiteSpace(request.Scope) ? "PRODUCT" : request.Scope.Trim().ToUpperInvariant();
        if (targetScope != "PRODUCT" && targetScope != "SERVICE")
            return BadRequest(new { message = "Scope inválido. Usa PRODUCT o SERVICE." });
        var requiredModule = targetScope == "SERVICE" ? "SERVICES" : "CATALOG";

        if (!await HasAccessTo(requiredModule, cancellationToken))
            return StatusCode(403, $"Módulo {requiredModule} no contratado.");

        try
        {
            var result = await generationService.RequestGenerationAsync(WorkspaceId, targetScope, cancellationToken);
            return Ok(new
            {
                status = result.Status.ToString(),
                message = "Generación de documento solicitada exitosamente.",
                sourceHash = result.SourceHash
            });
        }
        catch (DomainException ex)
        {
            return StatusCode(429, new { code = "RateLimit.Exceeded", message = ex.Message });
        }
    }

    private void QueueArtifactInvalidation(Guid workspaceId)
    {
        _taskQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, token) =>
        {
            using var scope = serviceProvider.CreateScope();
            var lifecycleDb = scope.ServiceProvider.GetRequiredService<NexFlowDbContext>();
            await using var lifecycle = await lifecycleDb.Database.BeginTransactionAsync(token);
            await TenantLifecycleLock.AcquireAsync(lifecycleDb, workspaceId, false, token);
            if (!await lifecycleDb.Workspaces.AnyAsync(w => w.Id == workspaceId && w.Status != NexFlow.Domain.Enums.WorkspaceStatus.Deleting, token)) return;
            var generationService = scope.ServiceProvider.GetRequiredService<ICatalogGenerationService>();

            await generationService.CheckAndInvalidateStaleArtifactsAsync(workspaceId, token);
        });
    }
}

public class GenerateArtifactRequest
{
    public string Scope { get; set; } = string.Empty;
}
