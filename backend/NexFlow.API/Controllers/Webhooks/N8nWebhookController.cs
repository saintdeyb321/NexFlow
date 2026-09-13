using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Domain.Entities.Catalog;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
        _webhookSecret = config["N8n:WebhookSecret"] ?? "nexflow-dev-secret-123";
    }

    [HttpPost("catalog-ready")]
    public async Task<IActionResult> OnCatalogReady([FromBody] CatalogReadyPayload payload, [FromHeader(Name = "X-NexFlow-Signature")] string providedSignature, CancellationToken cancellationToken)
    {
        // 1. FASE 5 - SPRINT 16: Validación Criptográfica HMAC
        if (string.IsNullOrWhiteSpace(providedSignature))
            return Unauthorized(new { message = "Firma de webhook ausente." });

        var payloadJson = JsonSerializer.Serialize(payload);
        var expectedSignature = ComputeHmacSha256(payloadJson, _webhookSecret);

        // En producción, n8n debe generar el HMACSHA256 del JSON usando el mismo secret
        if (providedSignature != expectedSignature && providedSignature != _webhookSecret) // Fallback temporal al secret directo para facilitar tus pruebas
        {
            return Unauthorized(new { message = "Firma HMAC inválida." });
        }

        // 2. Validación de completitud estructural
        if (payload == null || string.IsNullOrWhiteSpace(payload.PdfUrl) || payload.WorkspaceId == Guid.Empty || string.IsNullOrWhiteSpace(payload.GenerationId))
        {
            return BadRequest(new { message = "Payload inválido o incompleto." });
        }

        var scope = string.IsNullOrWhiteSpace(payload.Scope) ? "PRODUCT" : payload.Scope.ToUpperInvariant();
        var artifact = await _artifactRepository.GetCurrentArtifactAsync(payload.WorkspaceId, scope, cancellationToken);

        // 3. FASE 5 - SPRINT 16: Idempotencia y Blindaje contra Race Conditions
        // Exigimos que el estado sea Generating y que el ID de generación COINCIDA EXACTAMENTE
        if (artifact != null && artifact.Status == CatalogArtifactStatus.Generating)
        {
            if (artifact.GenerationId != payload.GenerationId)
            {
                // Un proceso de n8n viejo intentó responder, lo ignoramos de forma segura
                return Ok(new { message = "Generación obsoleta ignorada. Hay una más reciente en proceso." });
            }

            // Validamos que el SourceHash también coincida para asegurar integridad de la data
            if (artifact.SourceHash != payload.SourceHash)
            {
                artifact.MarkAsFailed();
                await _artifactRepository.SaveArtifactAsync(artifact, cancellationToken);
                return BadRequest(new { message = "Inconsistencia de Hash detectada." });
            }

            // 4. Marcamos el artefacto como vigente
            artifact.CompleteGeneration(payload.PdfUrl); // Si tu dominio soporta ImageUrl, agrégalo aquí
            await _artifactRepository.SaveArtifactAsync(artifact, cancellationToken);

            return Ok(new { message = "Artefacto enlazado y asegurado con éxito." });
        }

        return Ok(new { message = "Idempotencia: El artefacto ya estaba procesado o no existía generación activa." });
    }

    private static string ComputeHmacSha256(string data, string secret)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hashBytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hashBytes);
    }
}

public class CatalogReadyPayload
{
    public Guid WorkspaceId { get; set; }
    public string GenerationId { get; set; } = string.Empty;
    public string Scope { get; set; } = string.Empty;
    public string SourceHash { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string PdfUrl { get; set; } = string.Empty;
}