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
        // 1. Ordenamos todo de forma determinista para que el JSON siempre se arme igual
        var orderedCategories = categories
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.DisplayOrder, c.IsActive })
            .ToList();

        var orderedItems = items
            .OrderBy(i => i.Id)
            .Select(i => new {
                i.Id,
                i.Name,
                i.CategoryId,
                i.Type,
                i.PriceMinorUnits,
                i.IsActive,
                i.ImageUrl
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

        // 4. Lo convertimos a un texto legible (Ej: "A72F91...")
        var stringBuilder = new StringBuilder();
        foreach (var b in hashBytes)
        {
            stringBuilder.Append(b.ToString("x2"));
        }

        return stringBuilder.ToString();
    }
}