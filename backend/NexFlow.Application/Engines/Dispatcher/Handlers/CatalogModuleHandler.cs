using NexFlow.Application.Abstractions;

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

    public async Task<string> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode != "READ")
            return "SISTEMA: Capacidad no soportada por el módulo CATALOG.";

        var products = await _catalogRepository.GetProductsAsync(workspaceId, cancellationToken);
        var activeProducts = products.Where(p => p.IsActive).ToList();

        if (!activeProducts.Any())
            return "SISTEMA: Informa cortésmente que actualmente no hay productos disponibles en el catálogo.";

        // 🔥 SPRINT 3 (Auditoría #20): Límite de Contexto. Si hay más de 10, resumimos.
        if (activeProducts.Count > 10)
        {
            var categories = activeProducts
                .Select(p => string.IsNullOrWhiteSpace(p.Category) ? "Generales" : p.Category)
                .Distinct()
                .ToList();

            var categoriesText = string.Join(", ", categories);
            return $"SISTEMA: El catálogo tiene {activeProducts.Count} productos divididos en estas categorías: {categoriesText}. Pídele amablemente al cliente que especifique qué categoría o tipo de producto busca para darle opciones y precios exactos.";
        }

        var productsText = string.Join("\n", activeProducts.Select(p =>
        {
            var desc = !string.IsNullOrWhiteSpace(p.Description) ? $" - {p.Description}" : "";
            return $"- {p.Name}: {p.Currency} {p.PriceMinorUnits / 100m}{desc}";
        }));

        return $@"SISTEMA: Utiliza la siguiente lista de productos y sus precios para responder la duda del cliente. NO ofrezcas productos que no estén en esta lista:
{productsText}";
    }
}