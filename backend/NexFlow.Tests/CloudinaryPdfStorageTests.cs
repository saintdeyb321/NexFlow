using CloudinaryDotNet;
using NexFlow.Application.Common;
using NexFlow.Infrastructure.Gateways.Storage;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace NexFlow.Tests;

public sealed class CloudinaryPdfStorageTests
{
    [Fact]
    public async Task Upload_uses_unique_complete_public_id_raw_resource_and_no_overwrite()
    {
        var folder = $"nexflow/workspaces/{Guid.NewGuid():D}/artifacts/SERVICE";
        var name = $"{Guid.NewGuid():N}.pdf";
        var publicId = $"{folder}/{name}";
        using var transport = new StorageTransport(JsonSerializer.Serialize(new { public_id = publicId, secure_url = "https://storage.example.test/pdf", resource_type = "raw", version = 1 }));
        using var client = new HttpClient(transport);
        var cloudinary = new Cloudinary(new Account("unit-test", "key", "secret"));
        cloudinary.Api.Client = client;
        var storage = new CloudinaryFileStorage(cloudinary);
        using var stream = Fakes.CatalogFixture.Pdf();
        var asset = await storage.UploadPdfAsync(stream, name, folder, default);
        Assert.Equal(publicId, asset.PublicId);
        Assert.Equal("https://storage.example.test/pdf", asset.Url);
        Assert.EndsWith("/raw/upload", transport.Url);
        Assert.Contains(publicId, transport.Body);
        Assert.Equal("false", transport.Fields["overwrite"]);
        Assert.Equal(folder, transport.Fields["asset_folder"]);
        Assert.Equal("false", transport.Fields["use_asset_folder_as_public_id_prefix"]);
    }

    [Theory]
    [InlineData("ok")]
    [InlineData("not found")]
    public async Task Delete_uses_exact_raw_id_and_accepts_idempotent_not_found(string result)
    {
        using var transport = new StorageTransport(JsonSerializer.Serialize(new { result }));
        using var client = new HttpClient(transport);
        var cloudinary = new Cloudinary(new Account("unit-test", "key", "secret"));
        cloudinary.Api.Client = client;
        var storage = new CloudinaryFileStorage(cloudinary);
        var publicId = $"nexflow/workspaces/{Guid.NewGuid():D}/artifacts/PRODUCT/{Guid.NewGuid():N}.pdf";
        await storage.DeletePdfAsync(publicId, default);
        Assert.EndsWith("/raw/destroy", transport.Url);
        Assert.Contains(publicId, transport.Body);
        Assert.Contains("invalidate", transport.Body);
    }

    [Fact]
    public async Task External_storage_error_is_dependency_failure()
    {
        using var transport = new StorageTransport("{\"error\":{\"message\":\"Temporarily unavailable\"}}", HttpStatusCode.ServiceUnavailable);
        using var client = new HttpClient(transport);
        var cloudinary = new Cloudinary(new Account("unit-test", "key", "secret"));
        cloudinary.Api.Client = client;
        var storage = new CloudinaryFileStorage(cloudinary);
        using var stream = Fakes.CatalogFixture.Pdf();
        await Assert.ThrowsAsync<ArtifactDependencyException>(() => storage.UploadPdfAsync(stream, "test.pdf", "test", default));
    }

    private sealed class StorageTransport(string response, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public string Url { get; private set; } = string.Empty;
        public string Body { get; private set; } = string.Empty;
        public Dictionary<string, string> Fields { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Url = request.RequestUri!.ToString();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (request.Content is MultipartContent multipart)
                foreach (var part in multipart)
                    if (part.Headers.ContentDisposition?.Name is { } name)
                        Fields[name.Trim('"')] = await part.ReadAsStringAsync(cancellationToken);
            return new(status) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
