namespace NexFlow.Application.Abstractions;

public interface ICatalogHashService
{
    string ComputeContentHash(object content);
}
