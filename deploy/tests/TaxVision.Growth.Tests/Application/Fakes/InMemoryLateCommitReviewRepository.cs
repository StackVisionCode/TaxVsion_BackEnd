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

    /// <summary>Huella de la reserva de cada revisión (la base la toma de CodeReservations).</summary>
    internal Dictionary<Guid, string> SnapshotByReservation { get; } = [];

    public Task<LateCommitReviewTarget?> FindTargetAsync(Guid reviewId, CancellationToken ct = default)
    {
        var review = _reviews.FirstOrDefault(r => r.Id == reviewId);
        return Task.FromResult(
            review is null || !SnapshotByReservation.TryGetValue(review.CodeReservationId, out var snapshot)
                ? null
                : new LateCommitReviewTarget(
                    review.Id,
                    review.TenantId,
                    review.CodeReservationId,
                    review.PaymentSource,
                    review.RelatedPaymentId,
                    review.SourceEventId,
                    snapshot,
                    review.ResolvedAtUtc
                )
        );
    }

    public Task<LateCommitReview?> GetForResolutionAsync(Guid reviewId, CancellationToken ct = default) =>
        Task.FromResult(_reviews.FirstOrDefault(r => r.Id == reviewId));
}
