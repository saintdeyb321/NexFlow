using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
using NexFlow.Domain.Exceptions;
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

    // Las categorías son necesarias tanto para Productos como para Servicios
    private async Task<bool> HasAccessToCategories(CancellationToken ct)
    {
        var activeModules = await _entitlementService.GetAvailableModuleCodesAsync(WorkspaceId, ct);
        return activeModules.Contains("CATALOG") || activeModules.Contains("SERVICES");
    }

    // ==========================================
    // CATEGORÍAS (Compartidas)
    // ==========================================
    [HttpGet("categories")]
    public async Task<IActionResult> GetCategories(CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "No tienes módulos contratados que utilicen categorías.");
        var categories = await _catalogRepository.GetCategoriesAsync(WorkspaceId, cancellationToken);
        return Ok(categories);
    }

    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] CatalogCategoryDto category, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "Acceso denegado.");

        if (string.IsNullOrEmpty(category.Id)) category.Id = Guid.NewGuid().ToString();
        await _catalogRepository.SaveCategoryAsync(WorkspaceId, category, cancellationToken);
        return Ok(category);
    }

    [HttpPut("categories/{categoryId}")]
    public async Task<IActionResult> UpdateCategory(string categoryId, [FromBody] CatalogCategoryDto category, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "Acceso denegado.");

        category.Id = categoryId;
        await _catalogRepository.SaveCategoryAsync(WorkspaceId, category, cancellationToken);
        return Ok(category);
    }

    [HttpDelete("categories/{categoryId}")]
    public async Task<IActionResult> DeleteCategory(string categoryId, CancellationToken cancellationToken)
    {
        if (!await HasAccessToCategories(cancellationToken)) return StatusCode(403, "Acceso denegado.");

        // 🔥 Protección: No borrar si tiene ítems (productos o servicios)
        var items = await _catalogRepository.GetItemsByCategoryAsync(WorkspaceId, categoryId, cancellationToken);
        if (items.Any()) return BadRequest(new { message = "No puedes eliminar una categoría que contiene productos o servicios." });

        await _catalogRepository.DeleteCategoryAsync(WorkspaceId, categoryId, cancellationToken);
        return NoContent();
    }

    // =======================================================
    // PRODUCTS (Módulo Licenciado Separadamente)
    // =======================================================
    [HttpGet]
    public async Task<IActionResult> GetProducts(CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        var allItems = await _catalogRepository.GetItemsAsync(WorkspaceId, cancellationToken);
        var products = allItems.Where(i => i.Type.ToUpperInvariant() == "PRODUCT").ToList();

        return Ok(products);
    }

    [HttpPost]
    public async Task<IActionResult> SaveProduct([FromBody] CatalogItemDto product, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        // 🔥 ESTRICTO: Obligamos a que sea PRODUCT
        product.Type = "PRODUCT";
        if (string.IsNullOrEmpty(product.Id)) product.Id = Guid.NewGuid().ToString();

        if (string.IsNullOrEmpty(product.CategoryId)) product.CategoryId = Guid.Empty.ToString();

        await _catalogRepository.SaveItemAsync(WorkspaceId, product, cancellationToken);
        return Ok(product);
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> DeleteProduct(string productId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        // 🔥 ESTRICTO: Solo borramos si el item es un PRODUCTO
        var item = await _catalogRepository.GetItemByIdAsync(WorkspaceId, productId, cancellationToken);
        if (item != null && item.Type.ToUpperInvariant() == "PRODUCT")
        {
            await _catalogRepository.DeleteItemAsync(WorkspaceId, productId, cancellationToken);
        }

        return NoContent();
    }
    // ==========================================
    // ARTEFACTOS Y PDF (Sprints 5 y 6)
    // ==========================================
    [HttpGet("artifact")]
    public async Task<IActionResult> GetArtifactStatus(
        [FromServices] ICatalogArtifactRepository artifactRepository,
        CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        var artifact = await artifactRepository.GetCurrentArtifactAsync(WorkspaceId, cancellationToken);
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
        [FromServices] ICatalogGenerationService generationService,
        CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        try
        {
            var result = await generationService.RequestGenerationAsync(WorkspaceId, cancellationToken);
            return Ok(new
            {
                status = result.Status.ToString(),
                message = "Generación de artefacto solicitada exitosamente.",
                sourceHash = result.SourceHash
            });
        }
        catch (DomainException ex)
        {
            // Protegido contra spam o exceso del límite diario de 3 generaciones
            return StatusCode(429, new { code = "RateLimit.Exceeded", message = ex.Message });
        }
    }

}