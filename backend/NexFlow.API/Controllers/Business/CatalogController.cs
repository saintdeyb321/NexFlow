using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;

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

    [HttpGet]
    public async Task<IActionResult> GetProducts(CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");
        var products = await _catalogRepository.GetProductsAsync(WorkspaceId, cancellationToken);
        return Ok(products);
    }

    [HttpPost]
    public async Task<IActionResult> SaveProduct([FromBody] ProductDto product, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");

        // Si no tiene ID, es un producto nuevo. Le asignamos uno.
        if (string.IsNullOrEmpty(product.Id)) product.Id = Guid.NewGuid().ToString();

        await _catalogRepository.SaveProductAsync(WorkspaceId, product, cancellationToken);
        return Ok(product);
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> DeleteProduct(string productId, CancellationToken cancellationToken)
    {
        if (!await HasAccessTo("CATALOG", cancellationToken)) return StatusCode(403, "Módulo CATALOG no contratado.");
        await _catalogRepository.DeleteProductAsync(WorkspaceId, productId, cancellationToken);
        return NoContent();
    }
}