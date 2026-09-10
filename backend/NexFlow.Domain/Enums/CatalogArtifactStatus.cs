namespace NexFlow.Domain.Entities.Catalog;

public enum CatalogArtifactStatus
{
    NotGenerated, // Nunca se ha creado
    Generating,   // n8n/Gemini están trabajando en ello ahora mismo
    Current,      // El PDF está listo y coincide con la BD actual
    Stale,        // STALE: El negocio cambió un precio/producto, el PDF quedó viejo
    Failed        // n8n falló al generar
}