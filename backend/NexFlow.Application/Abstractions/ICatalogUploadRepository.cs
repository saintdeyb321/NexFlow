using NexFlow.Application.Features.Catalog.Uploads;
using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.Application.Abstractions;

public interface ICatalogUploadRepository
{
    Task BeginAsync(CatalogArtifact artifact, PdfUploadOperation operation, CancellationToken ct);
    Task<CatalogArtifact> PublishAsync(PdfUploadOperation operation, StoredPdfAsset asset, string sourceHash, CancellationToken ct);
    Task<PdfUploadOperation?> TryClaimCleanupAsync(PdfUploadOperation operation, bool cancelUpload, CancellationToken ct);
    Task RecordCleanupAsync(PdfUploadOperation operation, bool deleted, CancellationToken ct);
    Task<IReadOnlyList<PdfUploadOperation>> GetDueCleanupAsync(DateTime now, int limit, CancellationToken ct);
}
