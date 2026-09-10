using System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities.Catalog;

public class CatalogGenerationUsage : Entity
{
    public Guid WorkspaceId { get; private set; }
    public DateTime Date { get; private set; } // Se guarda solo la fecha (Ej: 2026-09-07)
    public int GenerationCount { get; private set; }

    // 🔥 REGLA DE NEGOCIO: Máximo 3 generaciones de IA por día por negocio.
    public const int MaxGenerationsPerDay = 3;

    private CatalogGenerationUsage() { }

    public static CatalogGenerationUsage Create(Guid workspaceId, DateTime date)
    {
        return new CatalogGenerationUsage
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Date = date.Date,
            GenerationCount = 0
        };
    }

    public void Increment()
    {
        if (GenerationCount >= MaxGenerationsPerDay)
        {
            throw new DomainException($"Has alcanzado el límite máximo de {MaxGenerationsPerDay} generaciones de catálogo por día.");
        }
        GenerationCount++;
    }
}