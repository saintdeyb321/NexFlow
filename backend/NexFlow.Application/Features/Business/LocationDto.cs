using System.ComponentModel.DataAnnotations;

namespace NexFlow.Application.Features.Business;

public record LocationDto(
    string? Id,

    [Required(ErrorMessage = "El nombre de la sede es obligatorio.")]
    string Name,

    [Required(ErrorMessage = "La dirección física es obligatoria.")]
    string Address,

    string? Reference,

    [Url(ErrorMessage = "El enlace del mapa debe ser una URL válida.")]
    [RegularExpression(@"^https?:\/\/(www\.)?google\.[a-z.]+\/maps.*|https?:\/\/maps\.app\.goo\.gl\/.*$",
        ErrorMessage = "Debe ser un enlace válido de Google Maps (ej: https://maps.app.goo.gl/...).")]
    string? MapUrl,

    bool IsMain
);