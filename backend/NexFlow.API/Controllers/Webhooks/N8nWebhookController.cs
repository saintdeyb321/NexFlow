using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities.Catalog;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace NexFlow.API.Controllers.Webhooks;

[ApiController]
[Route("api/webhooks/n8n")]
[AllowAnonymous]
public class N8nWebhookController : ControllerBase
{
    private readonly ICatalogArtifactRepository _artifactRepository;
    private readonly string _webhookSecret;

    public N8nWebhookController(ICatalogArtifactRepository artifactRepository, IConfiguration config)
    {
        _artifactRepository = artifactRepository;
        // 🔥 SPRINT 9: N8n deberá enviar este token en el Header: X-NexFlow-Secret
        _webhookSecret = config["N8n:WebhookSecret"] ?? "nexflow-dev-secret-123";
    }

    [HttpPost("catalog-pdf-ready")]
    public async Task<IActionResult> OnPdfReady([FromBody] PdfReadyPayload payload, CancellationToken cancellationToken)
    {
        // 1. Validar la firma/secreto de seguridad
        if (!Request.Headers.TryGetValue("X-NexFlow-Secret", out var providedSecret) || providedSecret != _webhookSecret)
        {
            return Unauthorized(new { message = "Firma de webhook inválida." });
        }

        if (payload == null || string.IsNullOrWhiteSpace(payload.PdfUrl) || payload.WorkspaceId == Guid.Empty)
            return BadRequest(new { message = "Payload inválido o incompleto." });

        // 2. Usamos el Scope que nos devuelva n8n (por defecto 'PRODUCT' por seguridad)
        var scope = string.IsNullOrWhiteSpace(payload.Scope) ? "PRODUCT" : payload.Scope.ToUpperInvariant();

        var artifact = await _artifactRepository.GetCurrentArtifactAsync(payload.WorkspaceId, scope, cancellationToken);

        if (artifact != null && artifact.Status == CatalogArtifactStatus.Generating)
        {
            // 3. Marcamos el artefacto como vigente
            artifact.CompleteGeneration(payload.PdfUrl);
            await _artifactRepository.SaveArtifactAsync(artifact, cancellationToken);

            return Ok(new { message = "Artefacto enlazado con éxito." });
        }

        return Ok(new { message = "No hay generación pendiente o el artefacto ya estaba al día." });
    }
}

public class PdfReadyPayload
{
    public Guid WorkspaceId { get; set; }
    public string Scope { get; set; } = string.Empty; // PRODUCT, SERVICE, COMBINED
    public string PdfUrl { get; set; } = string.Empty;
}