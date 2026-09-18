namespace NexFlow.Application.Features.Knowledge;

public enum KnowledgeTopic
{
    Profile,
    Locations,
    BusinessHours,
    Offerings,
    Faqs
}

public class KnowledgeQuery
{
    public KnowledgeTopic Topic { get; init; }
    public string? LocationId { get; init; }
    public string? SearchTerm { get; init; }
}

public class KnowledgeResult
{
    public bool Found { get; init; }
    public string Facts { get; init; } = string.Empty;
    public KnowledgeTopic Source { get; init; }
    public string? LocationId { get; init; }
}