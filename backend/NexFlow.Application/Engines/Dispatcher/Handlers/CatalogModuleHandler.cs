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

        // 🔥 Auditoría (Sprint 3.2): Lectura optimizada usando GetActiveProductsAsync
        var activeProducts = await _catalogRepository.GetActiveProductsAsync(workspaceId, cancellationToken);
        var productsList = activeProducts.ToList();

        // 🔥 Auditoría (Sprint 3.1): Filtrado Multi-Sede
        if (request.Parameters.TryGetValue("locationId", out var locObj) && locObj is string locationId && !string.IsNullOrWhiteSpace(locationId))
        {
            productsList = productsList.Where(p =>
                p.AvailableAtLocations == null ||
                !p.AvailableAtLocations.Any() ||
                p.AvailableAtLocations.Contains(locationId)).ToList();
        }

        if (!productsList.Any())
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, "Informa cortésmente que actualmente no hay productos disponibles en el catálogo para la sede seleccionada.", false, Array.Empty<string>());

        // 🔥 Auditoría (Sprint 3.2): Búsqueda por relevancia
        var searchTerms = string.Join(" ", request.Parameters.Values)
                                .ToLowerInvariant()
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (searchTerms.Any(t => t.Length > 2)) // Ignoramos preposiciones cortas
        {
            var scoredProducts = productsList
                .Select(p => new
                {
                    Product = p,
                    Score = searchTerms.Count(term =>
                        p.Name.ToLowerInvariant().Contains(term) ||
                        (p.Category?.ToLowerInvariant().Contains(term) ?? false) ||
                        (p.Description?.ToLowerInvariant().Contains(term) ?? false))
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Product)
                .ToList();

            if (scoredProducts.Any())
            {
                productsList = scoredProducts.Take(5).ToList(); // Limitamos a los 5 más relevantes
            }
        }

        // Mantenemos tu lógica original de fallback a categorías si la lista sigue siendo muy grande
        if (productsList.Count > 10)
        {
            var categories = productsList
                .Select(p => string.IsNullOrWhiteSpace(p.Category) ? "Generales" : p.Category)
                .Distinct()
                .ToList();

            var categoriesText = string.Join(", ", categories);
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, $"El catálogo tiene {productsList.Count} productos divididos en estas categorías: {categoriesText}. Pídele amablemente al cliente que especifique qué categoría o tipo de producto busca para darle opciones y precios exactos.", false, Array.Empty<string>());
        }

        var productsText = string.Join("\n", productsList.Select(p =>
        {
            var desc = !string.IsNullOrWhiteSpace(p.Description) ? $" - {p.Description}" : "";
            return $"- {p.Name}: {p.Currency} {p.PriceMinorUnits / 100m:0.00}{desc}";
        }));

        var responseText = $"Utiliza la siguiente lista de productos y sus precios para responder la duda del cliente. NO ofrezcas productos ni inventes precios que no estén explícitamente en esta lista:\n{productsText}";

        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, responseText, false, Array.Empty<string>());
    }
}