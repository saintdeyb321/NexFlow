using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Common;
using NexFlow.Application.Features.Catalog.Uploads;

namespace NexFlow.Infrastructure.Gateways.Storage;

public class CloudinaryFileStorage : IFileStorage
{
    private readonly Cloudinary _cloudinary;

    internal CloudinaryFileStorage(Cloudinary cloudinary)
    {
        _cloudinary = cloudinary;
        _cloudinary.Api.Secure = true;
    }

    public CloudinaryFileStorage(IConfiguration configuration)
    {
        var account = new Account(
            configuration["Cloudinary:CloudName"],
            configuration["Cloudinary:ApiKey"],
            configuration["Cloudinary:ApiSecret"]);

        _cloudinary = new Cloudinary(account);
        _cloudinary.Api.Secure = true;
    }

    public async Task<string> UploadImageAsync(Stream fileStream, string fileName, string folderPath, CancellationToken cancellationToken)
    {
        var uploadParams = new ImageUploadParams()
        {
            File = new FileDescription(fileName, fileStream),
            Folder = folderPath,
            Transformation = new Transformation().Width(800).Crop("limit").FetchFormat("webp").Quality("auto")
        };

        var uploadResult = await _cloudinary.UploadAsync(uploadParams, cancellationToken);

        if (uploadResult.Error != null)
        {
            throw new Exception($"Error en Cloudinary: {uploadResult.Error.Message}");
        }

        return uploadResult.SecureUrl.ToString();
    }

    public async Task<StoredPdfAsset> UploadPdfAsync(Stream fileStream, string fileName, string folderPath, CancellationToken cancellationToken)
    {
        var publicId = $"{folderPath}/{fileName}";
        var uploadParams = new RawUploadParams
        {
            File = new FileDescription(fileName, fileStream),
            AssetFolder = folderPath,
            PublicId = publicId,
            UseAssetFolderAsPublicIdPrefix = false,
            Overwrite = false
        };
        var uploadResult = await _cloudinary.UploadAsync(uploadParams, "raw", cancellationToken);
        if (uploadResult.Error != null || uploadResult.SecureUrl == null)
            throw new ArtifactDependencyException("No se pudo almacenar el PDF en Cloudinary.");
        if (uploadResult.PublicId != publicId) throw new ArtifactDependencyException("Identidad de almacenamiento inesperada.");
        return new(uploadResult.SecureUrl.ToString(), uploadResult.PublicId);
    }

    public async Task DeletePdfAsync(string publicId, CancellationToken cancellationToken)
    {
        var result = await _cloudinary.DestroyAsync(new DeletionParams(publicId)
        {
            ResourceType = ResourceType.Raw,
            Invalidate = true
        }).WaitAsync(cancellationToken);
        if (result.Error != null || result.Result is not ("ok" or "not found" or "not_found"))
            throw new ArtifactDependencyException("No se pudo confirmar la eliminación del PDF pendiente.");
    }
}
