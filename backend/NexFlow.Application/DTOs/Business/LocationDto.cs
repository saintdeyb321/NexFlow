namespace NexFlow.Application.DTOs.Business;

public record LocationDto(
    string Id, // ID del documento en Firestore
    string Name, // Ej: "Sede Huancayo"
    string Address,
    string Reference,
    bool IsMain
);