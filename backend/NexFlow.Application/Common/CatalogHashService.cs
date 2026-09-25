using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexFlow.Application.Abstractions;
// 🔥 NUEVOS NAMESPACES
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Application.Features.Services.DTOs;

namespace NexFlow.Application.Common;

public class CatalogHashService : ICatalogHashService
{
    public string ComputeHash(IEnumerable<BusinessCategoryDto> categories, IEnumerable<BusinessOfferingDto> items)
    {
        var orderedCategories = categories
            .OrderBy(c => c.Id)
            .Select(c => new { c.Id, c.Name, c.DisplayOrder, c.IsActive })
            .ToList();

        var orderedItems = items
            .OrderBy(i => i.Id)
            .Select(i => new {
                i.Id,
                i.Name,
                i.Description,
                i.CategoryId,
                i.Type,
                i.PriceMinorUnits,
                i.Currency,
                i.IsActive,
                i.ImageUrl,
                i.LocationScope,
                // 🔥 Cast seguro al nuevo ServiceDto
                DurationInMinutes = (i as ServiceDto)?.DurationInMinutes,
                RequiresReservation = (i as ServiceDto)?.RequiresReservation,
                LocationIds = i.LocationIds != null ? string.Join(",", i.LocationIds.OrderBy(l => l)) : ""
            })
            .ToList();

        var payload = new { Categories = orderedCategories, Items = orderedItems };
        var json = JsonSerializer.Serialize(payload);

        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(json);
        var hashBytes = sha256.ComputeHash(bytes);

        var stringBuilder = new StringBuilder();
        foreach (var b in hashBytes)
        {
            stringBuilder.Append(b.ToString("x2"));
        }

        return stringBuilder.ToString();
    }
}