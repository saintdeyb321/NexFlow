using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration;
using NexFlow.Application.Abstractions;

namespace NexFlow.Infrastructure.Gateways.Storage;

public class CloudinaryFileStorage : IFileStorage
{
    private readonly Cloudinary _cloudinary;

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
}