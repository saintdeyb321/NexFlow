using System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities;

public class Faq : Entity
{
    public Guid WorkspaceId { get; private set; }
    public string Question { get; private set; } = null!;
    public string Answer { get; private set; } = null!;
    public string? Category { get; private set; }
    public bool IsActive { get; private set; }

    private Faq() { }

    public static Faq Create(Guid workspaceId, string question, string answer, string? category)
    {
        if (string.IsNullOrWhiteSpace(question)) throw new DomainException("La pregunta es obligatoria.");
        if (string.IsNullOrWhiteSpace(answer)) throw new DomainException("La respuesta es obligatoria.");

        // 🛡️ REGLAS FILOSÓFICAS: Respuestas concisas para no saturar los tokens de la IA
        if (question.Length > 150) throw new DomainException("La pregunta no puede exceder los 150 caracteres.");
        if (answer.Length > 600) throw new DomainException("La respuesta debe ser concisa (máximo 600 caracteres).");

        return new Faq
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Question = question.Trim(),
            Answer = answer.Trim(),
            Category = category,
            IsActive = true
        };
    }
}