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
    Unavailable
}

public class KnowledgeQuery
{
    public KnowledgeTopic Topic { get; init; }
    public string? LocationId { get; init; }
    public string? SearchTerm { get; init; }
}

public class KnowledgeResult
{
    public KnowledgeStatus Status { get; init; }
    public bool Found => Status == KnowledgeStatus.Found;
    public string Facts { get; init; } = string.Empty;
    public KnowledgeTopic Source { get; init; }
    public string? LocationId { get; init; }

    public string ToResponse() => Status switch
    {
        KnowledgeStatus.Found => Facts,
        KnowledgeStatus.Unavailable => "No puedo consultar la información del negocio en este momento. Por favor, intenta nuevamente en unos minutos.",
        _ => "No tengo información registrada que coincida con tu consulta. ¿Podrías precisar qué necesitas o indicar la sede?"
    };
}
