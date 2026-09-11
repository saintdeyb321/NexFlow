using System;
using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Engines.Dispatcher;
using System.Linq;
using System.Collections.Generic;
using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Engines.Dispatcher.Handlers;

public class ServiceModuleHandler : IModuleHandler
{
    public string ModuleCode => "SERVICES";

    private readonly ICatalogRepository _catalogRepository;
    private readonly ICatalogArtifactRepository _artifactRepository;

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

        var artifact = await _artifactRepository.GetCurrentArtifactAsync(workspaceId, "SERVICE", cancellationToken);
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

        // 🔥 SPRINT 10: Determinamos si fue una solicitud general o un filtro específico
        bool hasCategoryFilter = request.Parameters.TryGetValue("category", out var categoryObj) && !string.IsNullOrWhiteSpace(categoryObj?.ToString());
        bool isFullServicesRequest = !hasCategoryFilter;

        if (hasCategoryFilter)
        {
            var categorySearch = categoryObj!.ToString()!.ToLowerInvariant();
            var categoryFiltered = activeServices.Where(s =>
                categoryMap.ContainsKey(s.CategoryId) &&
                categoryMap[s.CategoryId].ToLowerInvariant() == categorySearch).ToList();

            if (categoryFiltered.Any())
                // Si buscan algo específico, no enviamos el PDF para no saturar el chat
                return BuildServicesResponse(categoryFiltered, categoryMap, request.CapabilityCode, null);
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
                pdfUrl = pdfUrl // Aquí sí enviamos el PDF por exceso de datos
            }));
        }

        // 🔥 SPRINT 10: Solo adjuntamos el PDF en el 'success' si no buscaban nada en específico.
        string? pdfToSend = isFullServicesRequest ? pdfUrl : null;

        return BuildServicesResponse(activeServices, categoryMap, request.CapabilityCode, pdfToSend);
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

        return new ModuleExecutionResult(true, ModuleCode, capabilityCode, JsonSerializer.Serialize(new { status = "success", pdfUrl = pdfUrl, services = resultData }));
    }
}