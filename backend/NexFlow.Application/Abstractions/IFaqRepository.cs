using NexFlow.Application.Features.Knowledge;

namespace NexFlow.Application.Abstractions;

public interface IFaqRepository
{
    Task<IEnumerable<FaqDto>> GetFaqsAsync(Guid workspaceId, CancellationToken cancellationToken);
    Task<FaqDto> SaveFaqAsync(Guid workspaceId, FaqDto faq, CancellationToken cancellationToken);
    Task DeleteFaqAsync(Guid workspaceId, string faqId, CancellationToken cancellationToken);
}