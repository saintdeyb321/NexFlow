namespace NexFlow.Application.Features.Knowledge;

public record FaqDto(
    string Id,
    string Question,
    string Answer,
    string Category
);