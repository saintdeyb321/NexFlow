namespace NexFlow.Application.Features.Knowledge;

public enum KnowledgeTopic
{
    Profile,
    Locations,
    BusinessHours,
    Offerings,
    Faqs,
    Products,
    Services
}

public enum KnowledgeStatus
{
    NotFound,
    Found,
    Unavailable,
    Denied
}

public class KnowledgeQuery
{
    public KnowledgeTopic Topic { get; init; }
    public string? LocationId { get; init; }
    public string? SearchTerm { get; init; }
    public int Offset { get; init; }
    public int Limit { get; init; } = 5;
}

public class KnowledgeResult
{
    public KnowledgeStatus Status { get; init; }
    public bool Found => Status == KnowledgeStatus.Found;
    public string Facts { get; init; } = string.Empty;
    public KnowledgeTopic Source { get; init; }
    public string? LocationId { get; init; }
    public IReadOnlyList<KnowledgeLocationChoice> LocationChoices { get; init; } = [];
    public IReadOnlyList<KnowledgeLocationChoice> References { get; init; } = [];
    public IReadOnlyList<NexFlow.Application.Features.Automation.Dialogue.Contracts.VerifiedOffering> Offerings { get; init; } = [];
    public bool HasMore { get; init; }
    public PublicBusinessProfile? Profile { get; init; }

    public string ToResponse() => Status switch
    {
        KnowledgeStatus.Found => Facts,
        KnowledgeStatus.Unavailable => "No puedo consultar la información del negocio en este momento. Por favor, intenta nuevamente en unos minutos.",
        KnowledgeStatus.Denied => "Esta información no está disponible para este negocio. Puedes solicitar un asesor.",
        _ => "No tengo información registrada que coincida con tu consulta. ¿Podrías precisar qué necesitas o indicar la sede?"
    };
}

public sealed record KnowledgeLocationChoice(string Id, string Name);
// Explicit public projection; TaxId, credentials and private configuration are deliberately absent.
public sealed record PublicBusinessProfile(string Name, string Description, string Email, string WhatsApp, string TimeZone);

