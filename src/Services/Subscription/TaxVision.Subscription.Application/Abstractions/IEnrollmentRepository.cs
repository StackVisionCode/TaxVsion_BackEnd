using TaxVision.Subscription.Domain.Enrollments;

namespace TaxVision.Subscription.Application.Abstractions;

public interface IEnrollmentRepository
{
    Task<SubscriptionEnrollment?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(SubscriptionEnrollment enrollment, CancellationToken ct = default);
}
