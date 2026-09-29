using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NexFlow.Application.Abstractions;

namespace NexFlow.Application.Common;

public sealed class CatalogHashService : ICatalogHashService
{
    public string ComputeContentHash(object content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content)))).ToLowerInvariant();
}
