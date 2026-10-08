using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Abstractions.Repositories;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Catalog.Uploads;
using NexFlow.Application.Features.Shared.DTOs;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Entities.System;
using NexFlow.Domain.ValueObjects;
using NexFlow.Infrastructure.Persistence.Firestore;

namespace NexFlow.Tests.Fakes;

public sealed class CatalogFixture
{
    public static ArtifactDesign Design => ArtifactDesign.Create("MODERN", "OCEAN", "BALANCED");
    public Guid WorkspaceId { get; } = Guid.NewGuid();
    public InMemoryFirestore Firestore { get; } = new();
    public FirestoreCatalogArtifactRepository Artifacts { get; }
    public FirestoreCatalogUploadRepository Uploads { get; }
    public Mock<ICatalogRepository> Catalog { get; } = new(MockBehavior.Strict);
    public Mock<IBusinessProfileRepository> Profiles { get; } = new(MockBehavior.Strict);
    public Mock<ILocationRepository> Locations { get; } = new(MockBehavior.Strict);
    public Mock<IFileStorage> Storage { get; } = new(MockBehavior.Strict);
    public Mock<IOutboxRepository> Outbox { get; } = new(MockBehavior.Strict);
    public Mock<IUnitOfWork> UnitOfWork { get; } = new(MockBehavior.Strict);
    public List<OutboxMessage> Messages { get; } = new();
    public CatalogGenerationService Service { get; }

    public CatalogFixture()
    {
        Artifacts = new(Firestore.Db);
        Uploads = new(Firestore.Db);
        Catalog.Setup(r => r.GetActiveCategoriesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BusinessCategoryDto>());
        Catalog.Setup(r => r.GetItemsByTypeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BusinessOfferingDto>());
        Profiles.Setup(r => r.GetProfileAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BusinessProfileDto("Real business", "123", "real@example.test", "51999999999", "Facts"));
        Locations.Setup(r => r.GetLocationsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<LocationDto>());
        Outbox.Setup(r => r.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>())).Callback<OutboxMessage, CancellationToken>((m, _) => { lock (Messages) Messages.Add(m); }).Returns(Task.CompletedTask);
        UnitOfWork.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        Storage.Setup(r => r.UploadPdfAsync(It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream _, string name, string folder, CancellationToken _) => new StoredPdfAsset($"https://storage.example.test/{folder}/{name}", $"{folder}/{name}"));
        Storage.Setup(r => r.DeletePdfAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Service = new(Catalog.Object, Artifacts, Artifacts, new CatalogHashService(), Profiles.Object, Locations.Object,
            Storage.Object, Uploads, Outbox.Object, UnitOfWork.Object, NullLogger<CatalogGenerationService>.Instance);
    }

    public string ArtifactPath(string scope = "PRODUCT", Guid? workspace = null) => $"workspaces/{workspace ?? WorkspaceId}/catalogArtifacts/current_{scope.ToLowerInvariant()}";
    public string UsagePath => $"workspaces/{WorkspaceId}/catalogGenerationUsages/{DateTime.UtcNow:yyyy-MM-dd}";
    public int Usage => (int)(Firestore.Read(UsagePath)?.Fields["GenerationCount"].IntegerValue ?? 0);
    public string OperationPath(PdfUploadOperation op) => $"catalogPdfUploads/{op.WorkspaceId:N}_{op.Scope}_{op.Id}";
    public Task<CatalogArtifact> Generate(string scope = "PRODUCT", bool replace = true, Guid? workspace = null)
        => Service.RequestGenerationAsync(workspace ?? WorkspaceId, scope, Design, replace, CancellationToken.None);
    public Task<CatalogArtifact> Upload(string scope = "PRODUCT", bool replace = true)
        => Service.UploadPdfAsync(WorkspaceId, scope, Pdf(), "brochure.pdf", "application/pdf", Pdf().Length, replace, CancellationToken.None);
    public static MemoryStream Pdf() => new("%PDF-1.7\nunit-test"u8.ToArray());
    public async Task<CatalogArtifact> SeedCurrent(string scope = "PRODUCT", Guid? workspace = null)
    {
        var artifact = CatalogArtifact.Initialize(workspace ?? WorkspaceId, scope);
        artifact.CompleteUpload("https://storage.example.test/previous.pdf", await Service.GetCurrentSourceHashAsync(artifact.WorkspaceId, scope, CancellationToken.None));
        await Artifacts.SaveArtifactAsync(artifact, CancellationToken.None);
        return artifact;
    }
    public async Task FinishGeneration(string scope = "PRODUCT")
    {
        var artifact = (await Artifacts.GetCurrentArtifactAsync(WorkspaceId, scope, CancellationToken.None))!;
        artifact.MarkAsFailed();
        await Artifacts.SaveArtifactAsync(artifact, CancellationToken.None);
    }
}
