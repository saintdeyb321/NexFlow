using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class CatalogModuleHandler : IModuleHandler
{
    public string ModuleCode => "CATALOG";

    private readonly ICatalogRepository _catalogRepository;

    public CatalogModuleHandler(ICatalogRepository catalogRepository)
    {
        _catalogRepository = catalogRepository;
    }

    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode != "READ")
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, "Capacidad no soportada por el módulo CATALOG.", false, Array.Empty<string>());

        var products = await _catalogRepository.GetProductsAsync(workspaceId, cancellationToken);
        var activeProducts = products.Where(p => p.IsActive).ToList();

        if (!activeProducts.Any())
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, "Informa cortésmente que actualmente no hay productos disponibles en el catálogo.", false, Array.Empty<string>());

        if (activeProducts.Count > 10)
        {
            var categories = activeProducts
                .Select(p => string.IsNullOrWhiteSpace(p.Category) ? "Generales" : p.Category)
                .Distinct()
                .ToList();

            var categoriesText = string.Join(", ", categories);
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, $"El catálogo tiene {activeProducts.Count} productos divididos en estas categorías: {categoriesText}. Pídele amablemente al cliente que especifique qué categoría o tipo de producto busca para darle opciones y precios exactos.", false, Array.Empty<string>());
        }

        var productsText = string.Join("\n", activeProducts.Select(p =>
        {
            var desc = !string.IsNullOrWhiteSpace(p.Description) ? $" - {p.Description}" : "";
            return $"- {p.Name}: {p.Currency} {p.PriceMinorUnits / 100m}{desc}";
        }));

        var responseText = $"Utiliza la siguiente lista de productos y sus precios para responder la duda del cliente. NO ofrezcas productos que no estén en esta lista:{productsText}";

        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, responseText, false, Array.Empty<string>());
    }
}