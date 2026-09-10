using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;
using System.Linq;
using System.Collections.Generic;
using NexFlow.Domain.Entities.Catalog; // 🔥 Requerido

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class ServiceModuleHandler : IModuleHandler
{
    public string ModuleCode => "SERVICES";

    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogArtifactRepository _artifactRepository; // 🔥 Agregado

    public ServiceModuleHandler(ICatalogRepository catalogRepository, ICatalogArtifactRepository artifactRepository)
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
        var activeServices = activeItems.Where(i => i.Type.ToUpperInvariant() == "SERVICE").ToList();

        var categories = await _catalogRepository.GetActiveCategoriesAsync(workspaceId, cancellationToken);
        var categoryMap = categories.ToDictionary(c => c.Id, c => c.Name);

        bool isGlobalScope = request.Parameters.TryGetValue("locationScope", out var scopeObj) && scopeObj?.ToString() == "ALL";

        if (!isGlobalScope && request.Parameters.TryGetValue("locationId", out var locObj) && locObj is string locationId && !string.IsNullOrWhiteSpace(locationId))
        {
            activeServices = activeServices.Where(s =>
                s.AvailableAtLocations == null ||
                !s.AvailableAtLocations.Any() ||
                s.AvailableAtLocations.Contains(locationId)).ToList();
        }

        if (request.Parameters.TryGetValue("category", out var categoryObj) && !string.IsNullOrWhiteSpace(categoryObj?.ToString()))
        {
            var categorySearch = categoryObj.ToString()!.ToLowerInvariant();

            var categoryFiltered = activeServices.Where(s =>
                categoryMap.ContainsKey(s.CategoryId) &&
                categoryMap[s.CategoryId].ToLowerInvariant() == categorySearch).ToList();

            if (categoryFiltered.Any())
                return BuildServicesResponse(categoryFiltered, categoryMap, request.CapabilityCode, pdfUrl); // 🔥 Pasamos pdfUrl
        }

        if (!activeServices.Any())
            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new { status = "empty", message = "No hay servicios disponibles" }));

        if (activeServices.Count > 10)
        {
            var activeCategoryNames = activeServices
                .Select(s => categoryMap.ContainsKey(s.CategoryId) ? categoryMap[s.CategoryId] : "Generales")
                .Distinct()
                .ToList();

            return new ModuleExecutionResult(true, ModuleCode, request.CapabilityCode, JsonSerializer.Serialize(new
            {
                status = "too_many_results",
                totalCount = activeServices.Count,
                categories = activeCategoryNames,
                pdfUrl = pdfUrl 
            }));
        }

        return BuildServicesResponse(activeServices, categoryMap, request.CapabilityCode, pdfUrl);
    }

    private ModuleExecutionResult BuildServicesResponse(List<NexFlow.Application.Features.Business.CatalogItemDto> services, Dictionary<string, string> categoryMap, string capabilityCode, string? pdfUrl)
    {
        var resultData = services.Select(s => new
        {
            name = s.Name,
            category = categoryMap.ContainsKey(s.CategoryId) ? categoryMap[s.CategoryId] : "Generales",
            price = $"{s.Currency} {s.PriceMinorUnits / 100m:0.00}",
            durationMin = s.DurationInMinutes > 0 ? s.DurationInMinutes : (int?)null,
            requiresReservation = s.RequiresReservation,
            description = s.Description
        });

        // 🔥 Adjuntamos el PDF también en la respuesta exitosa
        return new ModuleExecutionResult(true, ModuleCode, capabilityCode, JsonSerializer.Serialize(new { status = "success", pdfUrl = pdfUrl, services = resultData }));
    }
}