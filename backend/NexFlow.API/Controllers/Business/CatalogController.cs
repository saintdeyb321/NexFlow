using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Exceptions;
using NexFlow.Domain.Entities.Catalog;
using System.Linq;

namespace NexFlow.API.Controllers.Business;

[ApiController]
[Route("api/catalog")]
[Authorize(Policy = "WorkspaceMember")]
public class CatalogController : ControllerBase
{
    private readonly ICatalogRepository _catalogRepository;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly IEntitlementService _entitlementService;

    public CatalogController(
        ICatalogRepository catalogRepository,
        IWorkspaceContext workspaceContext,
        IEntitlementService entitlementService)
    {
        _catalogRepository = catalogRepository;
        _workspaceContext = workspaceContext;
        _entitlementService = entitlementService;
    }

    private Guid WorkspaceId => _workspaceContext.CurrentWorkspaceId;

    private async Task<bool> HasAccessTo(string moduleCode, CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains(moduleCode.ToUpperInvariant());
    }

    private async Task<bool> HasAccessToCategories(CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains("CATALOG") || activeModules.Contains("SERVICES");
    }

    // ==========================================
    // CATEGORÍAS (Compartidas)
    // ==========================================
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories([FromQuery] string? scope, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "No tienes módulos contratados que utilicen categorías.");

        var categories = await _catalogRepository.GetCategoriesAsync(WorkspaceId, cancellationToken);

        if (!string.IsNullOrWhiteSpace(scope))
        {
            var targetScope = scope.ToUpperInvariant();
            categories = categories.Where(c => c.Scope == targetScope || c.Scope == "SHARED").ToList();
        }

        return Ok(categories);
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CatalogCategoryDto category, [FromServices] ICatalogGenerationService generationService, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "Acceso denegado.");

        if (string.IsNullOrEmpty(category.Id)) category.Id = Guid.NewGuid().ToString();
        await _catalogRepository.SaveCategoryAsync(WorkspaceId, category, cancellationToken);

        // 🔥 SPRINT 4: Disparador de invalidación STALE
        var currentWorkspaceId = WorkspaceId;
        _ = Task.Run(() => generationService.CheckAndInvalidateStaleArtifactsAsync(currentWorkspaceId, CancellationToken.None));

        return Ok(category);
    }

    [HttpPut("categories/{categoryId}")]
    public async Task<IActionResult> UpdateCategory(string categoryId, [FromBody] CatalogCategoryDto category, [FromServices] ICatalogGenerationService generationService, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "Acceso denegado.");

        category.Id = categoryId;
        await _catalogRepository.SaveCategoryAsync(WorkspaceId, category, cancellationToken);

        // 🔥 SPRINT 4: Disparador de invalidación STALE
        var currentWorkspaceId = WorkspaceId;
        _ = Task.Run(() => generationService.CheckAndInvalidateStaleArtifactsAsync(currentWorkspaceId, CancellationToken.None));

        return Ok(category);
    }

    [HttpDelete("categories/{categoryId}")]
    public async Task<IActionResult> DeleteCategory(string categoryId, [FromServices] ICatalogGenerationService generationService, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "Acceso denegado.");

        var items = await _catalogRepository.GetItemsByCategoryAsync(WorkspaceId, categoryId, cancellationToken);
        if (items.Any()) return BadRequest(new { message = "No puedes eliminar una categoría que contiene productos o servicios." });

        await _catalogRepository.DeleteCategoryAsync(WorkspaceId, categoryId, cancellationToken);

        // 🔥 SPRINT 4: Disparador de invalidación STALE
        var currentWorkspaceId = WorkspaceId;
        _ = Task.Run(() => generationService.CheckAndInvalidateStaleArtifactsAsync(currentWorkspaceId, CancellationToken.None));

        return NoContent();
    }

    // =======================================================
    // PRODUCTS (Módulo Licenciado Separadamente)
    // =======================================================
    [HttpGet]
    public async Task<IActionResult> GetProducts([FromQuery] string? locationId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        var allItems = await _catalogRepository.GetItemsAsync(WorkspaceId, cancellationToken);
        var products = allItems.Where(i => i.Type.ToUpperInvariant() == "PRODUCT");

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
    public async Task<IActionResult> SaveProduct([FromBody] CatalogItemDto product, [FromServices] ICatalogGenerationService generationService, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        product.Type = "PRODUCT";
        if (string.IsNullOrEmpty(product.Id)) product.Id = Guid.NewGuid().ToString();

        if (string.IsNullOrEmpty(product.CategoryId))
        {
            product.CategoryId = Guid.Empty.ToString();
        }
        else
        {
            var category = await _catalogRepository.GetCategoryByIdAsync(WorkspaceId, product.CategoryId, cancellationToken);
            if (category == null)
                return BadRequest(new { message = "La categoría asignada no existe." });

            if (category.Scope == "SERVICE")
                return BadRequest(new { message = "No puedes asignar un Producto a una categoría exclusiva de Servicios." });
        }

        await _catalogRepository.SaveItemAsync(WorkspaceId, product, cancellationToken);

        // 🔥 SPRINT 4: Disparador de invalidación STALE
        var currentWorkspaceId = WorkspaceId;
        _ = Task.Run(() => generationService.CheckAndInvalidateStaleArtifactsAsync(currentWorkspaceId, CancellationToken.None));

        return Ok(product);
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> DeleteProduct(string productId, [FromServices] ICatalogGenerationService generationService, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        var item = await _catalogRepository.GetItemByIdAsync(WorkspaceId, productId, cancellationToken);
        if (item != null && item.Type.ToUpperInvariant() == "PRODUCT")
        {
            await _catalogRepository.DeleteItemAsync(WorkspaceId, productId, cancellationToken);

            // 🔥 SPRINT 4: Disparador de invalidación STALE
            var currentWorkspaceId = WorkspaceId;
            _ = Task.Run(() => generationService.CheckAndInvalidateStaleArtifactsAsync(currentWorkspaceId, CancellationToken.None));
        }

        return NoContent();
    }

    // ==========================================
    // ARTEFACTOS Y PDF (Soporta Products y Services)
    // ==========================================
    [HttpGet("artifact")]
    public async Task<IActionResult> GetArtifactStatus(
        [FromQuery] string? scope,
        [FromServices] ICatalogArtifactRepository artifactRepository,
        CancellationToken cancellationToken)
    {
        var targetScope = string.IsNullOrWhiteSpace(scope) ? "PRODUCT" : scope.ToUpperInvariant();
        var requiredModule = targetScope == "SERVICE" ? "SERVICES" : "CATALOG";

        if (!await HasAccessTo(requiredModule, cancellationToken))
            return StatusCode(403, $"Módulo {requiredModule} no contratado.");

        var artifact = await artifactRepository.GetCurrentArtifactAsync(WorkspaceId, targetScope, cancellationToken);
        if (artifact == null)
        {
            return Ok(new { status = "NOT_GENERATED", pdfUrl = (string?)null });
        }

        return Ok(new
        {
            status = artifact.Status.ToString(),
            pdfUrl = artifact.PdfUrl,
            lastGeneratedAt = artifact.LastGeneratedAt
        });
    }

    [HttpPost("artifact/generate")]
    public async Task<IActionResult> GenerateArtifact(
        [FromBody] GenerateArtifactRequest request,
        [FromServices] ICatalogGenerationService generationService,
        CancellationToken cancellationToken)
    {
        var targetScope = string.IsNullOrWhiteSpace(request.Scope) ? "PRODUCT" : request.Scope.ToUpperInvariant();
        var requiredModule = targetScope == "SERVICE" ? "SERVICES" : "CATALOG";

        if (!await HasAccessTo(requiredModule, cancellationToken))
            return StatusCode(403, $"Módulo {requiredModule} no contratado.");

        try
        {
            var result = await generationService.RequestGenerationAsync(WorkspaceId, targetScope, cancellationToken);
            return Ok(new
            {
                status = result.Status.ToString(),
                message = "Generación de artefacto solicitada exitosamente.",
                sourceHash = result.SourceHash
            });
        }
        catch (DomainException ex)
        {
            return StatusCode(429, new { code = "RateLimit.Exceeded", message = ex.Message });
        }
    }
}

public class GenerateArtifactRequest
{
    public string Scope { get; set; } = string.Empty;
}