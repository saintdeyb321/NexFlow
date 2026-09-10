using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;
using System.Linq;
using System.Collections.Generic;
using NexFlow.Domain.Entities.Catalog; // 🔥 Requerido para CatalogArtifactStatus

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class CatalogModuleHandler : IModuleHandler
{
    public string ModuleCode => "CATALOG";

    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogArtifactRepository _artifactRepository; // 🔥 Agregado

    public CatalogModuleHandler(ICatalogRepository catalogRepository, ICatalogArtifactRepository artifactRepository)
    {
        _catalogRepository = catalogRepository;
        _artifactRepository = artifactRepository;
    }

    public string[] SupportedCapabilities => new[] { "READ" };

    public async Task<ModuleExecutionResult> ExecuteCapabilityAsync(Guid workspaceId, CapabilityRequest request, CancellationToken cancellationToken)
    {
        if (request.CapabilityCode != "READ")
            return new ModuleExecutionResult(false, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { error = "Capacidad no soportada" }));

        // 🔥 Obtenemos si hay un PDF generado y vigente
        var artifact = await _artifactRepository.GetCurrentArtifactAsync(workspaceId, cancellationToken);
        string? pdfUrl = artifact?.Status == CatalogArtifactStatus.Current ? artifact.PdfUrl : null;

        var activeItems = await _catalogRepository.GetActiveItemsAsync(workspaceId, cancellationToken);
        var categories = await _catalogRepository.GetActiveCategoriesAsync(workspaceId, cancellationToken);
        var categoryMap = categories.ToDictionary(c => c.Id, c => c.Name);

        var productsList = activeItems.Where(i => i.Type.ToUpperInvariant() == "PRODUCT").ToList();

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
                    CategoryName = categoryMap.ContainsKey(p.CategoryId) ? categoryMap[p.CategoryId].ToLowerInvariant() : "",
                    Score = searchTerms.Count(term =>
                        p.Name.ToLowerInvariant().Contains(term) ||
                        (categoryMap.ContainsKey(p.CategoryId) && categoryMap[p.CategoryId].ToLowerInvariant().Contains(term)) ||
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
            var activeCategoryNames = productsList
                .Select(p => categoryMap.ContainsKey(p.CategoryId) ? categoryMap[p.CategoryId] : "Generales")
                .Distinct()
                .ToList();

            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new
            {
                status = "too_many_results",
                totalCount = productsList.Count,
                categories = activeCategoryNames,
                pdfUrl = pdfUrl // 🔥 Adjuntamos el PDF a la IA
            }));
        }

        var resultData = productsList.Select(p => new
        {
            name = p.Name,
            category = categoryMap.ContainsKey(p.CategoryId) ? categoryMap[p.CategoryId] : "Generales",
            price = $"{p.Currency} {p.PriceMinorUnits / 100m:0.00}",
            description = p.Description
        });

        // 🔥 Adjuntamos el PDF también en la respuesta exitosa
        return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "success", pdfUrl = pdfUrl, products = resultData }));
    }
}