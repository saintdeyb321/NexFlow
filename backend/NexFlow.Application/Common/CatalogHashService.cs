using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;

namespace NexFlow.Application.Common;

public class CatalogHashService : ICatalogHashService
{
    public string ComputeHash(IEnumerable<CatalogCategoryDto> categories, IEnumerable<CatalogItemDto> items)
    {
        // 1. Ordenamos de forma determinista
        var orderedCategories = categories
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.DisplayOrder, c.IsActive })
            .ToList();

        var orderedItems = items
            .OrderBy(i => i.Id)
            .Select(i => new {
                i.Id,
                i.Name,
                i.Description, // 🔥 SPRINT 13: Detectar cambios en texto
                i.CategoryId,
                i.Type,
                i.PriceMinorUnits,
                i.Currency,    // 🔥 SPRINT 13: Detectar cambios en moneda
                i.IsActive,
                i.ImageUrl,
                i.DurationInMinutes, // 🔥 SPRINT 13: Detectar cambios en duración
                i.LocationScope,     // 🔥 SPRINT 13: Detectar cambios de Sedes
                LocationIds = i.LocationIds != null ? string.Join(",", i.LocationIds.OrderBy(l => l)) : ""
            })
            .ToList();

        var payload = new
        {
            Categories = orderedCategories,
            Items = orderedItems
        };

        // 2. Serializamos a JSON
        var json = JsonSerializer.Serialize(payload);

        // 3. Calculamos el SHA-256 (Hash Criptográfico)
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(json);
        var hashBytes = sha256.ComputeHash(bytes);

        // 4. Lo convertimos a texto hexadecimal
        var stringBuilder = new StringBuilder();
        foreach (var b in hashBytes)
        {
            stringBuilder.Append(b.ToString("x2"));
        }

        return stringBuilder.ToString();
    }
}