namespace NexFlow.Application.DTOs.Business;

public record BusinessProfileDto(
    string CommercialName,
    string TaxId, // RUC, CUIT, etc.
    string ContactEmail,
    string WhatsAppNumber,
    string Description
);