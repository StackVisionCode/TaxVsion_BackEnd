using TaxVision.Postmaster.Domain.Feedback;

namespace TaxVision.Postmaster.Application.Feedback;

public interface IUnmatchedDeliveryFeedbackRepository
{
    Task AddAsync(UnmatchedDeliveryFeedback feedback, CancellationToken ct = default);

    Task<bool> ExistsAsync(string source, string sourceEventId, CancellationToken ct = default);
}
