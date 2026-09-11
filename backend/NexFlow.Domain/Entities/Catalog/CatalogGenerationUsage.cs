using System;
using NexFlow.Domain.Exceptions;

namespace NexFlow.Domain.Entities.Catalog;

public class CatalogGenerationUsage : Entity
{
    public Guid WorkspaceId { get; private set; }
    public DateTime Date { get; private set; }
    public int GenerationCount { get; private set; }

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

    // 🔥 SPRINT 4: Reconstrucción
    public static CatalogGenerationUsage Restore(Guid id, Guid workspaceId, DateTime date, int count)
    {
        return new CatalogGenerationUsage
        {
            Id = id,
            WorkspaceId = workspaceId,
            Date = date,
            GenerationCount = count
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