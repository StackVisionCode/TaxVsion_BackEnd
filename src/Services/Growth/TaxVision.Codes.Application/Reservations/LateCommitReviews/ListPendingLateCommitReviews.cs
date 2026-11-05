using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Codes.Application.Reservations.LateCommitReviews;

/// <summary>Commits tardíos pendientes de revisión manual, de todos los tenants (solo PlatformAdmin).</summary>
public sealed record ListPendingLateCommitReviewsQuery(int Take);

public sealed record LateCommitReviewResponse(
    Guid Id,
    Guid TenantId,
    Guid CodeReservationId,
    string PaymentSource,
    Guid RelatedPaymentId,
    Guid SourceEventId,
    string ErrorCode,
    DateTime CreatedAtUtc
)
{
    public static LateCommitReviewResponse From(LateCommitReview review) =>
        new(
            review.Id,
            review.TenantId,
            review.CodeReservationId,
            review.PaymentSource,
            review.RelatedPaymentId,
            review.SourceEventId,
            review.ErrorCode,
            review.CreatedAtUtc
        );
}

public static class ListPendingLateCommitReviewsHandler
{
    public const int MaxTake = 200;

    public static async Task<Result<IReadOnlyList<LateCommitReviewResponse>>> Handle(
        ListPendingLateCommitReviewsQuery query,
        ILateCommitReviewRepository reviews,
        CancellationToken ct
    )
    {
        var take = Math.Clamp(query.Take, 1, MaxTake);
        var pending = await reviews.ListPendingAsync(take, ct);
        return Result.Success<IReadOnlyList<LateCommitReviewResponse>>(
            pending.Select(LateCommitReviewResponse.From).ToList()
        );
    }
}
