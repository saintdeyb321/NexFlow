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

    public StorageController(IFileStorage fileStorage, IWorkspaceContext workspaceContext)
    {
        _fileStorage = fileStorage;
        _workspaceContext = workspaceContext;
    }

    [HttpPost("upload")]
    // [DisableRequestSizeLimit] // Descomenta solo si vas a subir archivos gigantes, pero 5MB está cubierto por defecto.
    public async Task<IActionResult> UploadImage(IFormFile file, CancellationToken cancellationToken)
    {
        // 1. Validaciones Básicas de Seguridad
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No se ha enviado ningún archivo." });
        }

        // 2. Limitar tamaño a 5MB
        if (file.Length > 5 * 1024 * 1024)
        {
            return BadRequest(new { message = "La imagen supera el límite de 5MB." });
        }

        // 3. Validar que realmente sea una imagen
        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType.ToLowerInvariant()))
        {
            return BadRequest(new { message = "Formato no permitido. Solo JPG, PNG o WebP." });
        }

        try
        {
            using var stream = file.OpenReadStream();

            // 🔥 Multi-tenant Isolation: Guardamos las fotos en nexflow/workspaces/ID/
            var folderPath = $"nexflow/workspaces/{_workspaceContext.CurrentWorkspaceId}/catalog";

            // Generar nombre de archivo único para evitar colisiones
            var safeFileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";

            var secureUrl = await _fileStorage.UploadImageAsync(stream, safeFileName, folderPath, cancellationToken);

            return Ok(new { secureUrl });
        }
        catch (Exception ex)
        {
            // Loguear excepción internamente (aquí omitido por brevedad)
            return StatusCode(500, new { message = "Error interno al procesar la imagen." });
        }
    }
}