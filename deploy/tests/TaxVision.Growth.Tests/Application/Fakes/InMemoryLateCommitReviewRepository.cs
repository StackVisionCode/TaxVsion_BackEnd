using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Growth.Tests.Application.Fakes;

internal sealed class InMemoryLateCommitReviewRepository : ILateCommitReviewRepository
{
    private readonly List<LateCommitReview> _reviews = [];

    internal IReadOnlyList<LateCommitReview> Reviews => _reviews;

    public Task<bool> RecordAsync(LateCommitReview review, CancellationToken ct = default)
    {
        // Emula el índice único (CodeReservationId, SourceEventId).
        if (
            _reviews.Any(r =>
                r.CodeReservationId == review.CodeReservationId && r.SourceEventId == review.SourceEventId
            )
        )
            return Task.FromResult(false);

        _reviews.Add(review);
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<LateCommitReview>> ListPendingAsync(int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<LateCommitReview>>(
            _reviews.Where(r => r.ResolvedAtUtc is null).OrderBy(r => r.CreatedAtUtc).Take(take).ToList()
        );
}
