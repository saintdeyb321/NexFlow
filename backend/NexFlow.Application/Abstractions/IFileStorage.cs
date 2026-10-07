namespace NexFlow.Application.Abstractions;

public interface IFileStorage
{
    Task<string> UploadImageAsync(Stream fileStream, string fileName, string folderPath, CancellationToken cancellationToken);
    Task<string> UploadPdfAsync(Stream fileStream, string fileName, string folderPath, CancellationToken cancellationToken);
}
