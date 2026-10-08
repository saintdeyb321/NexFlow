using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.AI.Interpretation;
using NexFlow.Application.Features.Automation.ProcessMessage.Services.Flows;
using NexFlow.Application.Features.Business;
using NexFlow.Application.Features.Business.Locations;
using NexFlow.Application.Features.Knowledge;
using NexFlow.Application.Features.Notifications;
using NexFlow.Domain.Entities.Catalog;
using NexFlow.Domain.Enums;
using NexFlow.Tests.Fakes;
using Xunit;

namespace NexFlow.Tests;

public sealed class CatalogChatTests
{
    [Theory]
    [InlineData("PRODUCT", CatalogArtifactStatus.Current, true)]
    [InlineData("PRODUCT", CatalogArtifactStatus.Generating, false)]
    [InlineData("PRODUCT", CatalogArtifactStatus.Failed, false)]
    [InlineData("PRODUCT", CatalogArtifactStatus.Stale, false)]
    [InlineData("PRODUCT", CatalogArtifactStatus.NotGenerated, false)]
    [InlineData("SERVICE", CatalogArtifactStatus.Current, true)]
    [InlineData("SERVICE", CatalogArtifactStatus.Generating, false)]
    [InlineData("SERVICE", CatalogArtifactStatus.Failed, false)]
    [InlineData("SERVICE", CatalogArtifactStatus.Stale, false)]
    public async Task Automatic_whatsapp_document_response_requires_current_artifact(string scope, CatalogArtifactStatus status, bool sendsPdf)
    {
        var workspace = Guid.NewGuid();
        var artifact = CatalogArtifact.Initialize(workspace, scope);
        artifact.CompleteUpload("https://storage.example.test/brochure.pdf", "hash");
        if (status == CatalogArtifactStatus.Generating) artifact.MarkAsGenerating("hash", "generation", CatalogFixture.Design);
        if (status == CatalogArtifactStatus.Failed) artifact.MarkAsFailed();
        if (status == CatalogArtifactStatus.Stale) artifact.MarkAsStale();
        if (status == CatalogArtifactStatus.NotGenerated) artifact = CatalogArtifact.Initialize(workspace, scope);
        var service = new Mock<ICatalogGenerationService>(MockBehavior.Strict);
        service.Setup(s => s.GetArtifactAsync(workspace, scope, It.IsAny<CancellationToken>())).ReturnsAsync(artifact);
        var knowledge = new Mock<IKnowledgeService>(MockBehavior.Strict);
        knowledge.Setup(s => s.QueryAsync(workspace, It.IsAny<BusinessKnowledgeSnapshot>(), It.IsAny<KnowledgeQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new KnowledgeResult { Status = KnowledgeStatus.Found, Facts = "Real offering", Source = scope == "PRODUCT" ? KnowledgeTopic.Products : KnowledgeTopic.Services });
        var entitlements = new Mock<IEntitlementService>(MockBehavior.Strict);
        entitlements.Setup(s => s.GetAvailableModuleCodesAsync(workspace, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { "CATALOG", "SERVICES" });
        var notifications = new Mock<INotificationService>(MockBehavior.Strict);
        notifications.Setup(s => s.NotifyCatalogUnavailableAsync(workspace, scope, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var flow = new ChatFlow(knowledge.Object, entitlements.Object, Mock.Of<ILocationResolverService>(), NullLogger<ChatFlow>.Instance, service.Object, notifications.Object);
        var result = await flow.ProcessAsync(workspace, "Show catalog", new AiInterpretation
        { Intent = scope == "PRODUCT" ? ConversationIntent.ProductQuery : ConversationIntent.ServiceQuery, QueryKind = OfferingQueryKind.Broad }, null, default);
        Assert.Equal(sendsPdf, result.MediaUrl != null);
        if (sendsPdf) { Assert.Equal(artifact.PdfUrl, result.MediaUrl); Assert.Equal(scope, result.CatalogScope); }
        else Assert.Contains("Real offering", result.Text);
    }
}
