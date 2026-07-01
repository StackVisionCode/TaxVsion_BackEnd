using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Enrollments;
using TaxVision.Subscription.Infrastructure.Persistence;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class EnrollmentRepository(SubscriptionDbContext db) : IEnrollmentRepository
{
    public Task<SubscriptionEnrollment?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db.Enrollments.FirstOrDefaultAsync(e => e.Id == id, ct);

    public async Task AddAsync(SubscriptionEnrollment enrollment, CancellationToken ct = default) =>
        await db.Enrollments.AddAsync(enrollment, ct);
}
