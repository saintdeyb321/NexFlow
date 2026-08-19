namespace NexFlow.Application.DTOs.Knowledge;

public record FaqDto(
    string Id,
    string Question,
    string Answer,
    string Category
);