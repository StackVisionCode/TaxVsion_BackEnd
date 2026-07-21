using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Reservations.CommitReservation;

public static class CommitReservationHandler
{
    public static async Task<Result<CommitReservationResponse>> Handle(
        CommitReservationCommand command,
        ICodeDefinitionRepository definitions,
        ICodeReservationRepository reservations,
        ICodeRedemptionRepository redemptions,
        ICodeUsageCounterRepository usageCounters,
        IPaymentOutcomeVerifier paymentOutcomeVerifier,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Failure("Codes.CommitReservation.InvalidTenant", "TenantId is required.");

        if (command.ReservationId == Guid.Empty)
            return Failure("Codes.CommitReservation.InvalidReservation", "ReservationId is required.");

        if (command.SourceEventId == Guid.Empty)
            return Failure("Codes.CommitReservation.InvalidSourceEvent", "SourceEventId is required.");

        var paymentResult = PaymentReference.Create(command.PaymentSource, command.PaymentId);
        if (paymentResult.IsFailure)
            return Result.Failure<CommitReservationResponse>(paymentResult.Error);

        var snapshotResult = SnapshotHash.Create(command.SnapshotHash);
        if (snapshotResult.IsFailure)
            return Result.Failure<CommitReservationResponse>(snapshotResult.Error);

        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<CommitReservationResponse>(keyResult.Error);

        var fingerprint = OperationFingerprint.Create(
            command.TenantId,
            command.ReservationId,
            paymentResult.Value.Source,
            paymentResult.Value.PaymentId,
            snapshotResult.Value.Value,
            command.SourceEventId
        );

        return await idempotency.ExecuteAsync(
            "Codes.CommitReservation.v1",
            command.ReservationId,
            keyResult.Value,
            fingerprint,
            async operationCt =>
            {
                var reservation = await reservations.GetByIdAsync(command.TenantId, command.ReservationId, operationCt);
                if (reservation is null)
                    return Failure("Codes.CommitReservation.NotFound", "Reservation was not found.");

                if (reservation.Payment != paymentResult.Value)
                {
                    return Failure(
                        "Codes.CommitReservation.PaymentMismatch",
                        "The successful payment does not match the payment bound to the reservation."
                    );
                }

                if (reservation.SnapshotHash != snapshotResult.Value)
                {
                    return Failure(
                        "Codes.CommitReservation.SnapshotMismatch",
                        "The successful payment snapshot does not match the reserved offer snapshot."
                    );
                }

                // Providers can emit the same financial success as distinct event
                // envelopes. The payment reference is immutable on the reservation,
                // so an existing redemption is the authoritative business replay even
                // when EventId/Idempotency-Key differ.
                if (reservation.Status is CodeReservationStatus.Committed or CodeReservationStatus.Compensated)
                {
                    var existingRedemption = await redemptions.GetByReservationIdAsync(
                        command.TenantId,
                        reservation.Id,
                        operationCt
                    );
                    return existingRedemption is null
                        ? Failure(
                            "Codes.CommitReservation.InconsistentState",
                            "The reservation is committed but its redemption was not found."
                        )
                        : Result.Success(CommitReservationResponse.From(existingRedemption));
                }

                var definition = await definitions.GetApplicableByIdAsync(
                    command.TenantId,
                    reservation.CodeDefinitionId,
                    operationCt
                );
                if (definition is null)
                    return Failure("Codes.CommitReservation.CodeNotFound", "Code definition was not found.");

                var previousStatus = reservation.Status;
                var availabilityWasReleased = reservation.IsAvailabilityReleased;
                if (previousStatus == CodeReservationStatus.Expired)
                {
                    var verificationResult = await paymentOutcomeVerifier.VerifySucceededAsync(
                        command.TenantId,
                        reservation.Payment,
                        command.SourceEventId,
                        reservation.SnapshotHash,
                        operationCt
                    );
                    if (verificationResult.IsFailure)
                        return Result.Failure<CommitReservationResponse>(verificationResult.Error);
                }

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var counterSetResult = await CodeUsageCounterSet.LoadAsync(
                    definition,
                    command.TenantId,
                    reservation.Subject,
                    usageCounters,
                    nowUtc,
                    operationCt
                );
                if (counterSetResult.IsFailure)
                    return Result.Failure<CommitReservationResponse>(counterSetResult.Error);

                var redemptionResult = reservation.Commit(
                    keyResult.Value,
                    fingerprint,
                    command.SourceEventId,
                    allowLateCommit: previousStatus == CodeReservationStatus.Expired,
                    nowUtc
                );
                if (redemptionResult.IsFailure)
                    return Result.Failure<CommitReservationResponse>(redemptionResult.Error);

                var usageResult = counterSetResult.Value.CommitAll(
                    definition,
                    previousStatus == CodeReservationStatus.Expired && availabilityWasReleased,
                    nowUtc
                );
                if (usageResult.IsFailure)
                    return Result.Failure<CommitReservationResponse>(usageResult.Error);

                await redemptions.AddAsync(redemptionResult.Value, operationCt);
                return Result.Success(CommitReservationResponse.From(redemptionResult.Value));
            },
            ct
        );
    }

    private static Result<CommitReservationResponse> Failure(string code, string message) =>
        Result.Failure<CommitReservationResponse>(new Error(code, message));
}
