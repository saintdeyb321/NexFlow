using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.API.Uploads;

namespace NexFlow.API.Controllers;

[ApiController]
[Route("api/storage")]
[Authorize(Policy = "WorkspaceMember")]
public class StorageController : ControllerBase
{
    private readonly IFileStorage _fileStorage;
    private readonly IWorkspaceContext _workspaceContext;
    private readonly ILogger<StorageController> _logger; // 🔥 Agregamos Logger para no quedarnos ciegos

    public StorageController(IFileStorage fileStorage, IWorkspaceContext workspaceContext, ILogger<StorageController> logger)
    {
        _fileStorage = fileStorage;
        _workspaceContext = workspaceContext;
        _logger = logger;
    }

    [HttpPost("upload")]
    [RequestSizeLimit(ValidatedImage.MaxBytes + 64 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = ValidatedImage.MaxBytes)]
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No se ha enviado ningún archivo." });
        }

        if (file.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new { message = "La imagen supera el límite de 5MB." });
        }

        if (_workspaceContext.CurrentWorkspaceId == Guid.Empty) return Forbid();

        try
        {
            var validated = await ValidatedImage.ReadAsync(file, cancellationToken);
            using var stream = new MemoryStream(validated.Bytes, writable: false);
            var folderPath = $"nexflow/workspaces/{_workspaceContext.CurrentWorkspaceId}/catalog";
            var safeFileName = $"{Guid.NewGuid():N}{validated.Extension}";

            var secureUrl = await _fileStorage.UploadImageAsync(stream, safeFileName, folderPath, cancellationToken);

            return Ok(new { secureUrl });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (InvalidDataException)
        {
            return BadRequest(new { message = "Imagen inválida, incompleta o excesiva. Usa JPG, PNG o WebP estático de hasta 5 MB y 16 megapíxeles." });
        }
        catch (Exception ex)
        {
            // Provider exceptions can contain credentials or untrusted metadata.
            _logger.LogError("Image upload failed for {WorkspaceId}: {FailureType}", _workspaceContext.CurrentWorkspaceId, ex.GetType().Name);
            return StatusCode(500, new { message = "Error interno al procesar la imagen." });
        }
    }
}
