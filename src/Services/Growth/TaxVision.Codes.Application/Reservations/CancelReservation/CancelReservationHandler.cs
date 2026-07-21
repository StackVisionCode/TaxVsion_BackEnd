using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Reservations.CancelReservation;

public static class CancelReservationHandler
{
    public static async Task<Result<CancelReservationResponse>> Handle(
        CancelReservationCommand command,
        ICodeDefinitionRepository definitions,
        ICodeReservationRepository reservations,
        ICodeUsageCounterRepository usageCounters,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Failure("Codes.CancelReservation.InvalidTenant", "TenantId is required.");

        if (command.ReservationId == Guid.Empty)
            return Failure("Codes.CancelReservation.InvalidReservation", "ReservationId is required.");

        var paymentResult = PaymentReference.Create(command.PaymentSource, command.PaymentId);
        if (paymentResult.IsFailure)
            return Result.Failure<CancelReservationResponse>(paymentResult.Error);

        var reason = command.Reason ?? string.Empty;
        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<CancelReservationResponse>(keyResult.Error);

        var fingerprint = OperationFingerprint.Create(
            command.TenantId,
            command.ReservationId,
            paymentResult.Value.Source,
            paymentResult.Value.PaymentId,
            reason.Trim()
        );

        return await idempotency.ExecuteAsync(
            "Codes.CancelReservation.v1",
            command.ReservationId,
            keyResult.Value,
            fingerprint,
            async operationCt =>
            {
                var reservation = await reservations.GetByIdAsync(command.TenantId, command.ReservationId, operationCt);
                if (reservation is null)
                    return Failure("Codes.CancelReservation.NotFound", "Reservation was not found.");

                if (reservation.Payment != paymentResult.Value)
                {
                    return Failure(
                        "Codes.CancelReservation.PaymentMismatch",
                        "The failed payment does not match the payment bound to the reservation."
                    );
                }

                var definition = await definitions.GetApplicableByIdAsync(
                    command.TenantId,
                    reservation.CodeDefinitionId,
                    operationCt
                );
                if (definition is null)
                    return Failure("Codes.CancelReservation.CodeNotFound", "Code definition was not found.");

                var availabilityWasReleased = reservation.IsAvailabilityReleased;
                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                Result<CodeUsageCounterSet>? counterSetResult = null;
                if (!availabilityWasReleased)
                {
                    counterSetResult = await CodeUsageCounterSet.LoadAsync(
                        definition,
                        command.TenantId,
                        reservation.Subject,
                        usageCounters,
                        nowUtc,
                        operationCt
                    );
                    if (counterSetResult.IsFailure)
                        return Result.Failure<CancelReservationResponse>(counterSetResult.Error);
                }

                var cancelResult = reservation.Cancel(keyResult.Value, fingerprint, reason, nowUtc);
                if (cancelResult.IsFailure)
                    return Result.Failure<CancelReservationResponse>(cancelResult.Error);

                if (!availabilityWasReleased)
                {
                    var releaseResult = counterSetResult!.Value.ReleaseAll(definition, nowUtc);
                    if (releaseResult.IsFailure)
                        return Result.Failure<CancelReservationResponse>(releaseResult.Error);

                    var markReleasedResult = reservation.MarkAvailabilityReleased(nowUtc);
                    if (markReleasedResult.IsFailure)
                        return Result.Failure<CancelReservationResponse>(markReleasedResult.Error);
                }

                return Result.Success(CancelReservationResponse.From(reservation));
            },
            ct
        );
    }

    private static Result<CancelReservationResponse> Failure(string code, string message) =>
        Result.Failure<CancelReservationResponse>(new Error(code, message));
}
