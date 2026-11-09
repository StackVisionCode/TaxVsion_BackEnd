using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Feedback;
using TaxVision.Postmaster.Domain.Feedback;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

public sealed class UnmatchedDeliveryFeedbackRepository(PostmasterDbContext dbContext)
    : IUnmatchedDeliveryFeedbackRepository
{
    public async Task AddAsync(UnmatchedDeliveryFeedback feedback, CancellationToken ct = default) =>
        await dbContext.UnmatchedDeliveryFeedback.AddAsync(feedback, ct);

    public Task<bool> ExistsAsync(string source, string sourceEventId, CancellationToken ct = default) =>
        dbContext.UnmatchedDeliveryFeedback.AnyAsync(f => f.Source == source && f.SourceEventId == sourceEventId, ct);
}
