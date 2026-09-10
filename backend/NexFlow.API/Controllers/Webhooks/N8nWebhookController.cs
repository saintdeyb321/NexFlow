using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities.Catalog;

namespace NexFlow.API.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/n8n")]
[AllowAnonymous] // n8n llamará a este endpoint como servicio externo, sin JWT de usuario
public class N8nWebhookController : ControllerBase
{
    private readonly ICatalogArtifactRepository _artifactRepository;

    public N8nWebhookController(ICatalogArtifactRepository artifactRepository)
    {
        _artifactRepository = artifactRepository;
    }

    [HttpPost("catalog-pdf-ready")]
    public async Task<IActionResult> OnPdfReady([FromBody] PdfReadyPayload payload, CancellationToken cancellationToken)
    {
        // 1. Validaciones básicas
        if (payload == null || string.IsNullOrWhiteSpace(payload.PdfUrl) || payload.WorkspaceId == Guid.Empty)
        {
            return BadRequest(new { message = "Payload inválido o incompleto." });
        }

        // 2. Buscamos el Artefacto actual para este Workspace
        var artifact = await _artifactRepository.GetCurrentArtifactAsync(payload.WorkspaceId, cancellationToken);

        if (artifact != null && artifact.Status == CatalogArtifactStatus.Generating)
        {
            // 3. ¡Magia! Completamos la generación. Esto marca el Status como 'Current' 
            // y guarda el link que acabamos de recibir de n8n/Cloudinary
            artifact.CompleteGeneration(payload.PdfUrl);
            await _artifactRepository.SaveArtifactAsync(artifact, cancellationToken);

            return Ok(new { message = "Artefacto (PDF) enlazado y actualizado con éxito." });
        }

        return Ok(new { message = "No hay generación pendiente o el artefacto ya estaba al día." });
    }
}

public class PdfReadyPayload
{
    public Guid WorkspaceId { get; set; }
    public string PdfUrl { get; set; } = string.Empty;
}