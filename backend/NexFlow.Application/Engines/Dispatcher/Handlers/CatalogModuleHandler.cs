using System.Text.Json;
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
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { error = "Capacidad no soportada" }));

        var activeProducts = await _catalogRepository.GetActiveProductsAsync(workspaceId, cancellationToken);
        var productsList = activeProducts.ToList();

        if (request.Parameters.TryGetValue("locationId", out var locObj) && locObj is string locationId && !string.IsNullOrWhiteSpace(locationId))
        {
            productsList = productsList.Where(p =>
                p.AvailableAtLocations == null ||
                !p.AvailableAtLocations.Any() ||
                p.AvailableAtLocations.Contains(locationId)).ToList();
        }

        if (!productsList.Any())
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "empty", message = "No hay productos disponibles" }));

        var searchTerms = string.Join(" ", request.Parameters.Values)
                                .ToLowerInvariant()
                                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (searchTerms.Any(t => t.Length > 2))
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
                productsList = scoredProducts.Take(5).ToList();
            }
        }

        if (productsList.Count > 10)
        {
            var categories = productsList
                .Select(p => string.IsNullOrWhiteSpace(p.Category) ? "Generales" : p.Category)
                .Distinct()
                .ToList();

            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new
            {
                status = "too_many_results",
                totalCount = productsList.Count,
                categories
            }));
        }

        var resultData = productsList.Select(p => new
        {
            name = p.Name,
            price = $"{p.Currency} {p.PriceMinorUnits / 100m:0.00}",
            description = p.Description
        });

        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "success", products = resultData }));
    }
}