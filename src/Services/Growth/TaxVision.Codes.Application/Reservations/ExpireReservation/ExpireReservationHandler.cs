using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Reservations.ExpireReservation;

public static class ExpireReservationHandler
{
    public static async Task<Result<ExpireReservationResponse>> Handle(
        ExpireReservationCommand command,
        ICodeDefinitionRepository definitions,
        ICodeReservationRepository reservations,
        ICodeUsageCounterRepository usageCounters,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Failure("Codes.ExpireReservation.InvalidTenant", "TenantId is required.");

        if (command.ReservationId == Guid.Empty)
            return Failure("Codes.ExpireReservation.InvalidReservation", "ReservationId is required.");

        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<ExpireReservationResponse>(keyResult.Error);

        var fingerprint = OperationFingerprint.Create(command.TenantId, command.ReservationId);
        return await idempotency.ExecuteAsync(
            "Codes.ExpireReservation.v1",
            command.ReservationId,
            keyResult.Value,
            fingerprint,
            async operationCt =>
            {
                var reservation = await reservations.GetByIdAsync(command.TenantId, command.ReservationId, operationCt);
                if (reservation is null)
                    return Failure("Codes.ExpireReservation.NotFound", "Reservation was not found.");

                if (
                    reservation.Status
                        is CodeReservationStatus.Committed
                            or CodeReservationStatus.Cancelled
                            or CodeReservationStatus.Compensated
                    || reservation.IsAvailabilityReleased
                )
                    return Result.Success(ExpireReservationResponse.From(reservation));

                var definition = await definitions.GetApplicableByIdAsync(
                    command.TenantId,
                    reservation.CodeDefinitionId,
                    operationCt
                );
                if (definition is null)
                    return Failure("Codes.ExpireReservation.CodeNotFound", "Code definition was not found.");

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
                    return Result.Failure<ExpireReservationResponse>(counterSetResult.Error);

                if (reservation.Status == CodeReservationStatus.Active)
                {
                    var expireResult = reservation.Expire(nowUtc);
                    if (expireResult.IsFailure)
                        return Result.Failure<ExpireReservationResponse>(expireResult.Error);
                }

                var releaseResult = counterSetResult.Value.ReleaseAll(definition, nowUtc);
                if (releaseResult.IsFailure)
                    return Result.Failure<ExpireReservationResponse>(releaseResult.Error);

                var markResult = reservation.MarkAvailabilityReleased(nowUtc);
                if (markResult.IsFailure)
                    return Result.Failure<ExpireReservationResponse>(markResult.Error);

                return Result.Success(ExpireReservationResponse.From(reservation));
            },
            ct
        );
    }

    private static Result<ExpireReservationResponse> Failure(string code, string message) =>
        Result.Failure<ExpireReservationResponse>(new Error(code, message));
}
