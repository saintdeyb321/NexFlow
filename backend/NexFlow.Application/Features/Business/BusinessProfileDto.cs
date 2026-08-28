namespace NexFlow.Application.Features.Business;

public record BusinessProfileDto(
    string CommercialName,
    string TaxId, // RUC, CUIT, etc.
    string ContactEmail,
    string WhatsAppNumber,
    string Description,
    string TimeZone = "America/Lima"
);