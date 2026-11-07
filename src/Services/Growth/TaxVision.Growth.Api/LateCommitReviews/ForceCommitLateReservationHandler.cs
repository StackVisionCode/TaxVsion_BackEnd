using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Reservations.CommitReservation;
using TaxVision.Codes.Application.Reservations.LateCommitReviews;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Growth.Api.LateCommitReviews;

/// <summary>GRW-1: orquesta <see cref="ForceCommitLateReservationCommand"/> sin transacción propia: el commit corre con
/// el tenant de la reserva y en su transacción; la revisión se cierra en otra.</summary>
public static class ForceCommitLateReservationHandler
{
    public const string PaymentNotConfirmedCode = "Codes.LateCommitReview.PaymentNotConfirmed";

    // Sin transacción: cada paso abre la suya; el commit corre con el tenant de la reserva.
    [NonTransactional]
    public static async Task<Result<ForceCommitLateReservationResponse>> Handle(
        ForceCommitLateReservationCommand command,
        IMessageBus bus,
        ILateCommitReviewRepository reviews,
        CancellationToken ct
    )
    {
        var target = await reviews.FindTargetAsync(command.ReviewId, ct);
        if (target is null)
            return Failure("Codes.LateCommitReview.NotFound", "Late commit review was not found.");

        if (target.ResolvedAtUtc is not null)
            return Failure("Codes.LateCommitReview.AlreadyResolved", "This late commit review is already resolved.");

        var callerTenant = bus.TenantId;
        Result<CommitReservationResponse> committed;
        try
        {
            bus.TenantId = target.TenantId.ToString();
            committed = await bus.InvokeAsync<Result<CommitReservationResponse>>(
                new CommitReservationCommand(
                    target.TenantId,
                    target.CodeReservationId,
                    target.PaymentSource,
                    target.RelatedPaymentId,
                    target.SnapshotHash,
                    target.SourceEventId,
                    $"late-commit-review:{target.ReviewId:N}"
                ),
                ct
            );
        }
        finally
        {
            bus.TenantId = callerTenant;
        }

        if (committed.IsFailure)
            return committed.Error.Code == CommitReservationHandler.LateCommitPendingReviewCode
                ? Failure(
                    PaymentNotConfirmedCode,
                    "The payment owner does not confirm this payment; the review stays pending."
                )
                : Result.Failure<ForceCommitLateReservationResponse>(committed.Error);

        var resolved = await bus.InvokeAsync<Result>(
            new ResolveLateCommitReviewCommand(
                command.ReviewId,
                command.ActorUserId,
                command.Note,
                committed.Value.RedemptionId
            ),
            ct
        );
        return resolved.IsFailure
            ? Result.Failure<ForceCommitLateReservationResponse>(resolved.Error)
            : Result.Success(
                new ForceCommitLateReservationResponse(
                    command.ReviewId,
                    target.CodeReservationId,
                    committed.Value.RedemptionId
                )
            );
    }

    private static Result<ForceCommitLateReservationResponse> Failure(string code, string message) =>
        Result.Failure<ForceCommitLateReservationResponse>(new Error(code, message));
}
