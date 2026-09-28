using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NexFlow.Application.Abstractions;
using NexFlow.Application.Features.Business;
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
        _webhookSecret = config["N8n:WebhookSecret"] ?? throw new InvalidOperationException("Falta configurar N8n:WebhookSecret en appsettings o variables de entorno.");
    }

    [HttpPost("catalog-ready")]
    public async Task<IActionResult> OnCatalogReady(
        [FromHeader(Name = "X-NexFlow-Signature")] string providedSignature,
        [FromHeader(Name = "X-NexFlow-Timestamp")] string timestampString,
        [FromServices] ICatalogGenerationService generationService,
        CancellationToken cancellationToken)
    {
        // 🔥 SPRINT 11 (Auditoría): Protección estricta contra Replay Attacks y Hash de Raw Body
        if (string.IsNullOrWhiteSpace(providedSignature) || string.IsNullOrWhiteSpace(timestampString))
            return Unauthorized(new { message = "Firma o timestamp ausente." });

        if (!long.TryParse(timestampString, out long timestamp))
            return Unauthorized(new { message = "Timestamp inválido." });

        var requestTime = DateTimeOffset.FromUnixTimeSeconds(timestamp).UtcDateTime;
        if (Math.Abs((DateTime.UtcNow - requestTime).TotalMinutes) > 5)
            return Unauthorized(new { message = "Request expirado. Posible ataque de repetición (Replay Attack) bloqueado." });

        // Leemos el stream original tal cual vino por la red, sin reserializar (evita fallos de formato JSON)
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);

        var payloadToHash = $"{timestampString}.{rawBody}";
        var expectedSignature = ComputeHmacSha256(payloadToHash, _webhookSecret);

        if (providedSignature != expectedSignature)
            return Unauthorized(new { message = "Firma HMAC inválida. Intento de inyección bloqueado." });

        var payload = JsonSerializer.Deserialize<CatalogReadyPayload>(rawBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (payload == null || string.IsNullOrWhiteSpace(payload.PdfUrl) || payload.WorkspaceId == Guid.Empty || string.IsNullOrWhiteSpace(payload.GenerationId))
            return BadRequest(new { message = "Payload inválido o incompleto." });

        var scope = string.IsNullOrWhiteSpace(payload.Scope) ? "PRODUCT" : payload.Scope.Trim().ToUpperInvariant();
        if (scope != "PRODUCT" && scope != "SERVICE")
            return BadRequest(new { message = "Scope inválido." });

        var artifact = await _artifactRepository.GetCurrentArtifactAsync(payload.WorkspaceId, scope, cancellationToken);

        if (artifact != null && artifact.Status == CatalogArtifactStatus.Generating)
        {
            if (artifact.GenerationId != payload.GenerationId)
                return Ok(new { message = "Generación obsoleta ignorada. Hay una más reciente en proceso." });

            if (artifact.SourceHash != payload.SourceHash)
            {
                artifact.MarkAsFailed();
                await _artifactRepository.SaveArtifactAsync(artifact, cancellationToken);
                return BadRequest(new { message = "Inconsistencia de Hash detectada." });
            }

            var currentHash = await generationService.GetCurrentSourceHashAsync(payload.WorkspaceId, scope, cancellationToken);
            if (!string.Equals(currentHash, payload.SourceHash, StringComparison.Ordinal))
            {
                artifact.MarkAsFailed();
                await _artifactRepository.SaveArtifactAsync(artifact, cancellationToken);
                return Ok(new { message = "PDF invalidado: los datos del catálogo cambiaron durante la generación." });
            }

            artifact.CompleteGeneration(payload.PdfUrl);
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
    public string PdfUrl { get; set; } = string.Empty;
}