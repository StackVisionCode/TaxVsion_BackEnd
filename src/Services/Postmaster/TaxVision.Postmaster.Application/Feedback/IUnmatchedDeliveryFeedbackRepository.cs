using TaxVision.Postmaster.Domain.Feedback;

namespace TaxVision.Postmaster.Application.Feedback;

public interface IUnmatchedDeliveryFeedbackRepository
{
    Task AddAsync(UnmatchedDeliveryFeedback feedback, CancellationToken ct = default);
}
