using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Exceptions;
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
    private readonly IBackgroundTaskQueue _taskQueue; // 🔥 SPRINT 16: Cola segura inyectada

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

    private async Task<bool> HasAccessToCategories(CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains("CATALOG") || activeModules.Contains("SERVICES");
    }

    // ==========================================
    // CATEGORÍAS (Infraestructura Compartida)
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
    public async Task<IActionResult> CreateCategory([FromBody] ProductCategoryDto category, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "Acceso denegado.");

        if (string.IsNullOrEmpty(category.Id)) category.Id = Guid.NewGuid().ToString();
        await _catalogRepository.SaveCategoryAsync(WorkspaceId, category, cancellationToken);

        QueueArtifactInvalidation(WorkspaceId); // 🔥 SPRINT 16: Llamada segura a la cola

        return Ok(category);
    }

    [HttpPut("categories/{categoryId}")]
    public async Task<IActionResult> UpdateCategory(string categoryId, [FromBody] ProductCategoryDto category, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "Acceso denegado.");

        category.Id = categoryId;
        await _catalogRepository.SaveCategoryAsync(WorkspaceId, category, cancellationToken);

        QueueArtifactInvalidation(WorkspaceId);

        return Ok(category);
    }

    [HttpDelete("categories/{categoryId}")]
    public async Task<IActionResult> DeleteCategory(string categoryId, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "Acceso denegado.");

        var items = await _catalogRepository.GetItemsByCategoryAsync(WorkspaceId, categoryId, cancellationToken);
        if (items.Any()) return BadRequest(new { message = "No puedes eliminar una categoría que contiene productos o servicios." });

        await _catalogRepository.DeleteCategoryAsync(WorkspaceId, categoryId, cancellationToken);

        QueueArtifactInvalidation(WorkspaceId);

        return NoContent();
    }

    // =======================================================
    // PRODUCTS (Módulo CATALOG estrictamente)
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
    public async Task<IActionResult> SaveProduct([FromBody] ProductDto product, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        if (string.IsNullOrEmpty(product.Id)) product.Id = Guid.NewGuid().ToString();

        if (string.IsNullOrEmpty(product.CategoryId))
        {
            product.CategoryId = Guid.Empty.ToString();
        }
        else
        {
            var category = await _catalogRepository.GetCategoryByIdAsync(WorkspaceId, product.CategoryId, cancellationToken);
            if (category == null) return BadRequest(new { message = "La categoría asignada no existe." });
            if (category.Scope == "SERVICE") return BadRequest(new { message = "No puedes asignar un Producto a una categoría exclusiva de Servicios." });
        }

        await _catalogRepository.SaveItemAsync(WorkspaceId, product, cancellationToken);

        QueueArtifactInvalidation(WorkspaceId);

        return Ok(product);
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> DeleteProduct(string productId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        var item = await _catalogRepository.GetItemByIdAsync(WorkspaceId, productId, cancellationToken);
        if (item != null && item.Type.ToUpperInvariant() == "PRODUCT")
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
        [FromServices] ICatalogArtifactRepository artifactRepository,
        CancellationToken cancellationToken)
    {
        var targetScope = string.IsNullOrWhiteSpace(scope) ? "PRODUCT" : scope.ToUpperInvariant();
        var requiredModule = targetScope == "SERVICE" ? "SERVICES" : "CATALOG";

        if (!await HasAccessTo(requiredModule, cancellationToken))
            return StatusCode(403, $"Módulo {requiredModule} no contratado.");

        var artifact = await artifactRepository.GetCurrentArtifactAsync(WorkspaceId, targetScope, cancellationToken);

        if (artifact == null)
            return Ok(new { status = "NOT_GENERATED", pdfUrl = (string?)null });

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
                message = "Generación de documento solicitada exitosamente.",
                sourceHash = result.SourceHash
            });
        }
        catch (DomainException ex)
        {
            return StatusCode(429, new { code = "RateLimit.Exceeded", message = ex.Message });
        }
    }

    // 🔥 SPRINT 16: Método Helper para encolar de forma segura resolviendo el Scope
    private void QueueArtifactInvalidation(Guid workspaceId)
    {
        _taskQueue.QueueBackgroundWorkItemAsync(async (serviceProvider, token) =>
        {
            // Creamos un nuevo Scope porque la petición HTTP original ya habrá terminado
            using var scope = serviceProvider.CreateScope();
            var generationService = scope.ServiceProvider.GetRequiredService<ICatalogGenerationService>();

            await generationService.CheckAndInvalidateStaleArtifactsAsync(workspaceId, token);
        });
    }
}

public class GenerateArtifactRequest
{
    public string Scope { get; set; } = string.Empty;
}