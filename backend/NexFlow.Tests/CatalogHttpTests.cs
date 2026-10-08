using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.API.Controllers.Business;
using NexFlow.API.Middleware;
using NexFlow.API.Services.BackgroundServices;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Catalog.DTOs;
using NexFlow.Domain.Exceptions;
using System.Security.Claims;
using System.Text.Json;
using Xunit;

namespace NexFlow.Tests;

public sealed class CatalogHttpTests
{
    [Theory]
    [InlineData("validation", 400, "Validation.Invalid")]
    [InlineData("permission", 403, "Security.Forbidden")]
    [InlineData("conflict", 409, "Resource.Conflict")]
    [InlineData("quota", 429, "RateLimit.Exceeded")]
    [InlineData("dependency", 503, "Dependency.Unavailable")]
    [InlineData("not-found", 404, "Resource.NotFound")]
    public async Task Global_middleware_preserves_specific_status_and_code(string kind, int status, string code)
    {
        Exception failure = kind switch
        {
            "validation" => new DomainException("Invalid PDF"),
            "permission" => new UnauthorizedAccessException(),
            "conflict" => new ConcurrencyException("Generating"),
            "quota" => new CatalogQuotaExceededException("Daily quota exhausted"),
            "not-found" => new KeyNotFoundException("Business profile missing"),
            _ => new ArtifactDependencyException("Unavailable storage")
        };
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "user")], "test")) };
        context.Response.Body = new MemoryStream();
        var middleware = new GlobalExceptionMiddleware(_ => Task.FromException(failure), NullLogger<GlobalExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        Assert.Equal(status, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var payload = await JsonDocument.ParseAsync(context.Response.Body);
        Assert.Equal(code, payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Controller_does_not_convert_general_domain_validation_to_rate_limit()
    {
        var (controller, workspace, _) = Controller(["CATALOG"]);
        var service = new Mock<ICatalogGenerationService>(MockBehavior.Strict);
        service.Setup(s => s.RequestGenerationAsync(workspace, "PRODUCT", It.IsAny<NexFlow.Domain.ValueObjects.ArtifactDesign>(), false, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DomainException("Invalid business data"));
        await Assert.ThrowsAsync<DomainException>(() => controller.GenerateArtifact(Request("PRODUCT"), service.Object, default));
    }

    [Theory]
    [InlineData("PRODUCT", "SERVICES")]
    [InlineData("SERVICE", "CATALOG")]
    public async Task Generate_and_upload_check_requested_module_before_invoking_service(string scope, string available)
    {
        var (controller, _, _) = Controller([available]);
        var service = new Mock<ICatalogGenerationService>(MockBehavior.Strict);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.GenerateArtifact(Request(scope), service.Object, default)).StatusCode);
        Assert.Equal(403, Assert.IsType<ObjectResult>(await controller.UploadArtifact(new UploadArtifactRequest { Scope = scope }, service.Object, default)).StatusCode);
        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Upload_uses_authenticated_workspace_and_requested_scope()
    {
        var (controller, workspace, _) = Controller(["SERVICES"]);
        var service = new Mock<ICatalogGenerationService>(MockBehavior.Strict);
        using var stream = Fakes.CatalogFixture.Pdf();
        var file = new FormFile(stream, 0, stream.Length, "File", "brochure.pdf") { Headers = new HeaderDictionary(), ContentType = "application/pdf" };
        service.Setup(s => s.UploadPdfAsync(workspace, "SERVICE", It.IsAny<Stream>(), "brochure.pdf", "application/pdf", stream.Length, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(NexFlow.Domain.Entities.Catalog.CatalogArtifact.Initialize(workspace, "SERVICE"));
        Assert.IsType<OkObjectResult>(await controller.UploadArtifact(new UploadArtifactRequest { Scope = "SERVICE", ReplaceCurrent = true, File = file }, service.Object, default));
        service.VerifyAll();
    }

    private static GenerateArtifactRequest Request(string scope) => new() { Scope = scope, Design = new ArtifactDesignDto { VisualStyle = "MODERN", Palette = "OCEAN", Creativity = "BALANCED" } };
    private static (CatalogController Controller, Guid Workspace, Mock<IEntitlementService> Entitlements) Controller(string[] modules)
    {
        var workspace = Guid.NewGuid();
        var context = new Mock<IWorkspaceContext>();
        context.SetupGet(c => c.CurrentWorkspaceId).Returns(workspace);
        var entitlements = new Mock<IEntitlementService>();
        entitlements.Setup(s => s.GetAvailableModuleCodesAsync(workspace, It.IsAny<CancellationToken>())).ReturnsAsync(modules);
        return (new CatalogController(Mock.Of<ICatalogRepository>(), context.Object, entitlements.Object, Mock.Of<IBackgroundTaskQueue>()), workspace, entitlements);
    }
}
