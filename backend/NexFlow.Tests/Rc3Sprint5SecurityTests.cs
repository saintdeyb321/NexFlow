using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.API.Controllers;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.SuperAdmin.Workspaces;
using NexFlow.Infrastructure.DependencyInjection;
using NexFlow.Infrastructure.Workers;
using NexFlow.API.Controllers.Webhooks;
using NexFlow.API.Controllers.SuperAdmin;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Repositories;
using NexFlow.Domain.Exceptions;
using NexFlow.Application.Features.Business;
using NexFlow.Infrastructure.Persistence.PostgreSQL.Context;
using Microsoft.EntityFrameworkCore;
using SkiaSharp;
using Xunit;

namespace NexFlow.Tests;

public sealed class Rc3Sprint5SecurityTests
{
    [Theory]
    [InlineData("image/png", "photo.png", "89504E470D0A1A0A")]
    [InlineData("image/jpeg", "photo.jpg", "FFD8FFE00010")]
    [InlineData("image/webp", "photo.webp", "524946460C00000057454250")]
    [InlineData("image/png", "photo.png", "3C7363726970743E")]
    public async Task Invalid_or_truncated_image_never_reaches_storage(string mime, string name, string hex)
    {
        var storage = new Mock<IFileStorage>();
        storage.Setup(s => s.UploadImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), default)).ReturnsAsync("https://synthetic/image");
        var controller = Controller(storage.Object);
        using var stream = new MemoryStream(Convert.FromHexString(hex));
        var file = new FormFile(stream, 0, stream.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = mime };
        Assert.IsType<BadRequestObjectResult>(await controller.UploadImage(file, default));
        storage.Verify(s => s.UploadImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Never);
    }

    [Fact]
    public async Task Nuclear_deletion_is_unavailable_before_scheduling_any_job()
    {
        var scheduler = new Mock<ITenantDeletionScheduler>(MockBehavior.Strict);
        var handler = new DeleteClientCommandHandler(scheduler.Object, Mock.Of<ICurrentUser>());
        var result = await handler.Handle(new(Guid.NewGuid()), default);
        Assert.True(result.IsFailure); Assert.Equal("Workspace.DeletionUnavailable", result.Error.Code);
        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public void Production_host_does_not_start_nuclear_deletion_worker()
    {
        var registrations = new ServiceCollection().AddInfrastructure(new ConfigurationBuilder().Build());
        Assert.DoesNotContain(registrations, s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(TenantDeletionWorker));
    }

    [Fact]
    public async Task Deletion_hold_still_propagates_caller_cancellation_without_scheduling()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var scheduler = new Mock<ITenantDeletionScheduler>(MockBehavior.Strict);
        var handler = new DeleteClientCommandHandler(scheduler.Object, Mock.Of<ICurrentUser>());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.Handle(new(Guid.NewGuid()), cancellation.Token));
        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Nuclear_deletion_endpoint_returns_503_and_keeps_the_superadmin_policy()
    {
        var scheduler = new Mock<ITenantDeletionScheduler>(MockBehavior.Strict);
        var handler = new DeleteClientCommandHandler(scheduler.Object, Mock.Of<ICurrentUser>());
        var controller = new ClientsController(null!, null!);
        var response = Assert.IsType<ObjectResult>(await controller.DeleteClient(new(Guid.NewGuid()), handler, default));
        Assert.Equal(503, response.StatusCode);
        var json = System.Text.Json.JsonSerializer.SerializeToElement(response.Value);
        Assert.Equal("Workspace.DeletionUnavailable", json.GetProperty("code").GetString());
        Assert.Equal("SuperAdmin", typeof(ClientsController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Single().Policy);
        scheduler.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Direct_scheduler_cannot_bypass_the_hold_or_even_open_a_database()
    {
        await using var db = new NexFlowDbContext(new DbContextOptionsBuilder<NexFlowDbContext>().Options);
        var permissions = new Mock<IEntitlementService>(MockBehavior.Strict);
        var scheduler = new TenantDeletionScheduler(db, permissions.Object, NullLogger<TenantDeletionScheduler>.Instance);
        var error = await Assert.ThrowsAsync<ConcurrencyException>(() => scheduler.RequestAsync(Guid.NewGuid(), Guid.NewGuid(), default));
        Assert.Equal(TenantDeletionSafety.Unavailable.Description, error.Message); permissions.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("9223372036854775807")]
    [InlineData("-9223372036854775808")]
    public async Task Out_of_range_callback_timestamp_is_unauthorized_without_touching_stores(string timestamp)
    {
        var controller = new N8nWebhookController(Mock.Of<ICatalogArtifactRepository>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["N8n:WebhookSecret"] = "synthetic-test-secret" }).Build());
        await using var db = new NexFlowDbContext(new DbContextOptionsBuilder<NexFlowDbContext>().Options);
        Assert.IsType<UnauthorizedObjectResult>(await controller.OnCatalogReady("invalid", timestamp,
            Mock.Of<ICatalogGenerationService>(), db, default));
    }

    [Theory]
    [InlineData("png", "image/png", SKEncodedImageFormat.Png)]
    [InlineData("jpeg", "image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("webp", "image/webp", SKEncodedImageFormat.Webp)]
    public async Task Real_images_are_normalized_and_only_uploaded_to_the_authenticated_workspace(string extension, string mime, SKEncodedImageFormat format)
    {
        var workspace = Guid.NewGuid(); var storage = new Mock<IFileStorage>(); byte[]? uploaded = null; string? path = null; string? name = null;
        storage.Setup(s => s.UploadImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), default))
            .Returns(async (Stream input, string fileName, string folder, CancellationToken ct) =>
            { using var copy = new MemoryStream(); await input.CopyToAsync(copy, ct); uploaded = copy.ToArray(); path = folder; name = fileName; return "https://synthetic/image"; });
        using var stream = new MemoryStream(Image(format));
        var file = new FormFile(stream, 0, stream.Length, "file", $"untrusted.{extension}") { Headers = new HeaderDictionary(), ContentType = mime };
        Assert.IsType<OkObjectResult>(await Controller(storage.Object, workspace).UploadImage(file, default));
        Assert.Equal($"nexflow/workspaces/{workspace}/catalog", path); Assert.DoesNotContain("untrusted", name!);
        Assert.EndsWith(format == SKEncodedImageFormat.Jpeg ? ".jpg" : "." + extension, name);
        using var decoded = SKBitmap.Decode(uploaded); Assert.Equal(3, decoded.Width); Assert.Equal(2, decoded.Height);
        storage.Verify(s => s.UploadImageAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), default), Times.Once);
    }

    [Theory]
    [InlineData("mismatch")]
    [InlineData("extension")]
    [InlineData("suffix")]
    [InlineData("truncated-body")]
    [InlineData("huge-dimensions")]
    public async Task Adversarial_encoded_images_are_rejected_before_provider(string kind)
    {
        var bytes = Image(SKEncodedImageFormat.Jpeg); var mime = "image/jpeg"; var name = "photo.jpg";
        if (kind == "mismatch") mime = "image/png";
        if (kind == "extension") name = "photo.html";
        if (kind == "suffix") bytes = [.. bytes, .. "<script>bad()</script>"u8.ToArray()];
        if (kind == "truncated-body") bytes = [.. bytes.Take(bytes.Length / 2), 255, 217];
        if (kind == "huge-dimensions")
        {
            var sof = Enumerable.Range(0, bytes.Length - 10).First(i => bytes[i] == 255 && bytes[i + 1] is 192 or 194);
            bytes[sof + 5] = 0x40; bytes[sof + 6] = 0; bytes[sof + 7] = 0x40; bytes[sof + 8] = 0;
        }
        var storage = new Mock<IFileStorage>(MockBehavior.Strict); using var stream = new MemoryStream(bytes);
        var file = new FormFile(stream, 0, stream.Length, "file", name) { Headers = new HeaderDictionary(), ContentType = mime };
        Assert.IsType<BadRequestObjectResult>(await Controller(storage.Object).UploadImage(file, default)); storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Actual_stream_size_is_bounded_even_when_declared_length_lies()
    {
        var file = new Mock<IFormFile>(); file.SetupGet(f => f.Length).Returns(1); file.SetupGet(f => f.FileName).Returns("photo.png");
        file.SetupGet(f => f.ContentType).Returns("image/png");
        using var large = new TrackedStream(new byte[5 * 1024 * 1024 + 9000]); file.Setup(f => f.OpenReadStream()).Returns(large);
        var storage = new Mock<IFileStorage>(MockBehavior.Strict);
        Assert.IsType<BadRequestObjectResult>(await Controller(storage.Object).UploadImage(file.Object, default));
        Assert.InRange(large.PositionAtDispose, 1, 5 * 1024 * 1024 + 8192); storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Cancelled_upload_propagates_without_calling_provider()
    {
        using var ct = new CancellationTokenSource(); ct.Cancel(); using var stream = new MemoryStream(Image(SKEncodedImageFormat.Png));
        var file = new FormFile(stream, 0, stream.Length, "file", "photo.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        var storage = new Mock<IFileStorage>(MockBehavior.Strict);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Controller(storage.Object).UploadImage(file, ct.Token)); storage.VerifyNoOtherCalls();
    }

    private static byte[] Image(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(3, 2); bitmap.Erase(SKColors.CornflowerBlue);
        using var image = SKImage.FromBitmap(bitmap); using var data = image.Encode(format, 90); return data.ToArray();
    }

    [Fact]
    public async Task Oversized_declared_file_is_rejected_without_opening_a_stream()
    {
        var file = new Mock<IFormFile>(MockBehavior.Strict); file.SetupGet(f => f.Length).Returns(5 * 1024 * 1024 + 1);
        var storage = new Mock<IFileStorage>(MockBehavior.Strict);
        Assert.IsType<BadRequestObjectResult>(await Controller(storage.Object).UploadImage(file.Object, default));
        file.Verify(f => f.OpenReadStream(), Times.Never); storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Empty_workspace_is_rejected_before_storage()
    {
        using var stream = new MemoryStream(Image(SKEncodedImageFormat.Png));
        var file = new FormFile(stream, 0, stream.Length, "file", "photo.png") { Headers = new HeaderDictionary(), ContentType = "image/png" };
        var storage = new Mock<IFileStorage>(MockBehavior.Strict);
        Assert.IsType<ForbidResult>(await Controller(storage.Object, Guid.Empty).UploadImage(file, default)); storage.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Png, "image/png", "png")]
    [InlineData(SKEncodedImageFormat.Webp, "image/webp", "webp")]
    public async Task Truncated_compressed_pixels_are_rejected_even_with_a_valid_envelope(SKEncodedImageFormat format, string mime, string ext)
    {
        var original = Image(format);
        byte[] broken = format == SKEncodedImageFormat.Png ? [.. original.Take(original.Length / 2), .. original.TakeLast(12)] : original[..(original.Length / 2)];
        if (format == SKEncodedImageFormat.Webp) System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(4, 4), (uint)(broken.Length - 8));
        using var stream = new MemoryStream(broken);
        var file = new FormFile(stream, 0, stream.Length, "file", "photo." + ext) { Headers = new HeaderDictionary(), ContentType = mime };
        var storage = new Mock<IFileStorage>(MockBehavior.Strict);
        Assert.IsType<BadRequestObjectResult>(await Controller(storage.Object).UploadImage(file, default)); storage.VerifyNoOtherCalls();
    }

    private sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long PositionAtDispose { get; private set; }
        protected override void Dispose(bool disposing) { if (CanRead) PositionAtDispose = Position; base.Dispose(disposing); }
    }

    private static StorageController Controller(IFileStorage storage, Guid? workspace = null) => new(storage,
        Mock.Of<IWorkspaceContext>(w => w.CurrentWorkspaceId == (workspace ?? Guid.NewGuid())), NullLogger<StorageController>.Instance);
}
