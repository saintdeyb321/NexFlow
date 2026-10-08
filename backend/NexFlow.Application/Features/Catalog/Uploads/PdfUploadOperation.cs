namespace NexFlow.Application.Features.Catalog.Uploads;

public sealed record StoredPdfAsset(string Url, string PublicId);

public sealed record PdfUploadOperation(string Id, Guid WorkspaceId, string Scope, string PublicId,
    DateTime ReconcileAfter, bool StorageConfirmed = false, string? PdfUrl = null, string? CleanupToken = null)
{
    public static readonly TimeSpan UploadLease = TimeSpan.FromMinutes(15);
    public static PdfUploadOperation Create(Guid workspaceId, string scope, DateTime now)
    {
        if (workspaceId == Guid.Empty || scope is not ("PRODUCT" or "SERVICE")) throw new ArgumentException("Workspace o scope inválido.");
        var id = Guid.NewGuid().ToString("N");
        return new(id, workspaceId, scope, $"nexflow/workspaces/{workspaceId:D}/artifacts/{scope}/{id}.pdf", now.Add(UploadLease));
    }

    public void ValidateStorageIdentity()
    {
        if (!Guid.TryParseExact(Id, "N", out _) || WorkspaceId == Guid.Empty || Scope is not ("PRODUCT" or "SERVICE")
            || PublicId != $"nexflow/workspaces/{WorkspaceId:D}/artifacts/{Scope}/{Id}.pdf")
            throw new InvalidOperationException("La identidad del PDF no pertenece a la operación de upload.");
    }
}
