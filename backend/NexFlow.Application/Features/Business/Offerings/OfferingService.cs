using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Application.Features.Services.DTOs;
using System.Globalization;
using System.Text;

namespace NexFlow.Application.Features.Business.Offerings;

public sealed class OfferingService : IOfferingService
{
    private readonly ICatalogRepository _catalogRepo;

    public OfferingService(ICatalogRepository catalogRepo)
    {
        _catalogRepo = catalogRepo;
    }

    public async Task<IEnumerable<ProductDto>> GetProductsAsync(Guid workspaceId, string? locationId, string? query, CancellationToken ct)
    {
        // 🔥 SPRINT 11 (Auditoría): Descargamos SOLO los de tipo PRODUCT directamente desde Firestore.
        // Esto evita traer 5,000 servicios a memoria solo para descartarlos.
        var items = await _catalogRepo.GetItemsByTypeAsync(workspaceId, "PRODUCT", ct);

        var products = items.Where(i => i.IsActive).Select(MapToSpecific<ProductDto>);
        return FilterItems(products, locationId, query);
    }

    public async Task<IEnumerable<ServiceDto>> GetServicesAsync(Guid workspaceId, string? locationId, string? query, CancellationToken ct)
    {
        // 🔥 SPRINT 11 (Auditoría): Descargamos SOLO los de tipo SERVICE directamente desde Firestore.
        var items = await _catalogRepo.GetItemsByTypeAsync(workspaceId, "SERVICE", ct);

        var services = items
            .Where(i => i.IsActive)
            .Select(MapToSpecific<ServiceDto>);

        return FilterItems(services, locationId, query);
    }

    public async Task<ProductDto?> GetProductByIdAsync(Guid workspaceId, string productId, CancellationToken ct)
    {
        var item = await _catalogRepo.GetItemByIdAsync(workspaceId, productId, ct);
        if (item == null || item.Type != "PRODUCT" || !item.IsActive) return null;
        return MapToSpecific<ProductDto>(item);
    }

    public async Task<ServiceDto?> GetServiceByIdAsync(Guid workspaceId, string serviceId, CancellationToken ct)
    {
        var item = await _catalogRepo.GetItemByIdAsync(workspaceId, serviceId, ct);
        if (item == null || item.Type != "SERVICE" || !item.IsActive) return null;
        return MapToSpecific<ServiceDto>(item);
    }

    public async Task<bool> IsServiceAvailableAtLocationAsync(Guid workspaceId, string serviceId, string locationId, CancellationToken ct)
    {
        var item = await GetServiceByIdAsync(workspaceId, serviceId, ct);
        if (item == null || !item.IsActive) return false;

        if (string.Equals(item.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase))
            return true;

        return string.Equals(item.LocationScope, "SPECIFIC", StringComparison.OrdinalIgnoreCase) && item.LocationIds != null && item.LocationIds.Contains(locationId);
    }

    private IEnumerable<T> FilterItems<T>(IEnumerable<T> items, string? locationId, string? query) where T : BusinessOfferingDto
    {
        if (!string.IsNullOrWhiteSpace(locationId))
        {
            items = items.Where(i =>
                string.Equals(i.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase) ||
                (string.Equals(i.LocationScope, "SPECIFIC", StringComparison.OrdinalIgnoreCase) && i.LocationIds != null && i.LocationIds.Contains(locationId)));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var term = query.ToLowerInvariant();
            items = items.Where(i =>
                i.Name.ToLowerInvariant().Contains(term) ||
                (i.Description != null && i.Description.ToLowerInvariant().Contains(term)));
        }

        return items;
    }

    private static T MapToSpecific<T>(BusinessOfferingDto baseItem) where T : BusinessOfferingDto
    {
        return baseItem switch
        {
            T typedItem => typedItem,
            _ => throw new InvalidOperationException(
                $"El repositorio debe devolver {typeof(T).Name} con sus propiedades derivadas para {baseItem.Id}.")
        };
    }
}

public static class OfferingServiceReservationExtensions
{
    public static async Task<(ServiceDto? Service, string? Clarification)> ResolveReservationServiceAsync(
        this IOfferingService offerings, Guid workspaceId, string locationId, string serviceName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(locationId))
            return (null, "¿En qué sede deseas reservar?");

        if (string.IsNullOrWhiteSpace(serviceName))
            return (null, "¿Qué servicio deseas reservar en esa sede?");

        var services = (await offerings.GetServicesAsync(workspaceId, locationId, null, ct))
            .Where(s => s.Type == "SERVICE" && s.IsActive && s.RequiresReservation && s.DurationInMinutes >= 5)
            .Where(s => string.Equals(s.LocationScope, "ALL", StringComparison.OrdinalIgnoreCase)
                || (string.Equals(s.LocationScope, "SPECIFIC", StringComparison.OrdinalIgnoreCase) && s.LocationIds != null && s.LocationIds.Contains(locationId)))
            .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
        var term = NormalizeServiceName(serviceName, stripPrefixes: true);
        var matches = services.Where(s => NormalizeServiceName(s.Name) == term).ToList();
        if (matches.Count == 0 && term.Length > 0)
            matches = services.Where(s => NormalizeServiceName(s.Name).Contains(term, StringComparison.Ordinal)
                || term.Contains(NormalizeServiceName(s.Name), StringComparison.Ordinal)).ToList();

        return matches.Count switch
        {
            1 => (matches[0], null),
            0 => (null, "No encontré ese servicio reservable en esta sede.\n\n" + FormatReservationOptions(services)),
            _ => (null, "Hay varios servicios que coinciden con lo indicado:\n" +
                string.Join("\n", matches.Take(5).Select(s => $"• {s.Name}")) +
                (matches.Count > 5 ? "\nHay más coincidencias; indica el nombre completo." : "") +
                "\n\n¿Cuál de estos servicios deseas reservar?")
        };
    }

    internal static string NormalizeServiceName(string value, bool stripPrefixes = false)
    {
        var characters = value.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            .Select(c => char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ').ToArray();
        var normalized = string.Join(" ", new string(characters).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (!stripPrefixes) return normalized;
        string[] prefixes = ["vale", "ok", "bueno", "quiero reservar", "quiero el", "quiero", "el servicio de",
            "el servicio", "servicio de", "reservar el", "reservar", "el de", "el"];
        bool removed;
        do
        {
            removed = false;
            foreach (var prefix in prefixes)
                if (normalized.StartsWith(prefix + " ", StringComparison.Ordinal))
                {
                    normalized = normalized[(prefix.Length + 1)..];
                    removed = true;
                    break;
                }
        } while (removed);
        return normalized;
    }

    internal static string FormatReservationOptions(IReadOnlyList<ServiceDto> services, int offset = 0)
    {
        if (offset < 0 || offset >= services.Count) offset = 0;
        if (services.Count == 0)
            return "En esta sede no hay servicios reservables disponibles. ¿Deseas cambiar de sede?";
        if (services.Count == 1)
            return $"En esta sede, actualmente el único servicio reservable disponible es {services[0].Name}.\nPuedes elegirlo o cambiar de sede. ¿Qué opción prefieres?";
        return "Servicios reservables disponibles en esta sede:\n" +
            string.Join("\n", services.Skip(offset).Take(5).Select(s => $"• {s.Name}")) +
            (services.Count > 5 ? "\nHay más opciones; puedes pedir ver más o el catálogo." : "") +
            "\n\n¿Cuál servicio deseas reservar?";
    }
}
