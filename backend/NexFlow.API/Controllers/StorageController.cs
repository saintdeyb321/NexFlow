using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;

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

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLowerInvariant()))
        {
            return BadRequest(new { message = "Formato no permitido. Solo JPG, PNG o WebP." });
        }

        try
        {
            using var stream = file.OpenReadStream();
            var folderPath = $"nexflow/workspaces/{_workspaceContext.CurrentWorkspaceId}/catalog";
            var safeFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

            var secureUrl = await _fileStorage.UploadImageAsync(stream, safeFileName, folderPath, cancellationToken);

            return Ok(new { secureUrl });
        }
        catch (Exception ex)
        {
            // 🔥 CORRECCIÓN: Ahora logueamos el error exacto en la consola de C#
            _logger.LogError(ex, "Fallo crítico subiendo imagen a Cloudinary para el Workspace {WorkspaceId}", _workspaceContext.CurrentWorkspaceId);
            return StatusCode(500, new { message = "Error interno al procesar la imagen." });
        }
    }
}