namespace NexFlow.Application.Features.Business;

public record LocationDto(
    string? Id,         // 🔥 CORRECCIÓN: Nulable para creaciones nuevas (evita el Error 400)
    string Name,
    string Address,
    string? Reference,  // 🔥 CORRECCIÓN: Nulable por si el cliente no pone referencia
    string? MapUrl,     // 🔥 NUEVO: URL de Google Maps para la IA
    bool IsMain
);