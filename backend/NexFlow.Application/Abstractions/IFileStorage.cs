namespace NexFlow.Application.Abstractions;

using NexFlow.Application.Features.Catalog.Uploads;

public interface IFileStorage
{
    Task<string> UploadImageAsync(Stream fileStream, string fileName, string folderPath, CancellationToken cancellationToken);
    Task<StoredPdfAsset> UploadPdfAsync(Stream fileStream, string fileName, string folderPath, CancellationToken cancellationToken);
    Task DeletePdfAsync(string publicId, CancellationToken cancellationToken);
}
