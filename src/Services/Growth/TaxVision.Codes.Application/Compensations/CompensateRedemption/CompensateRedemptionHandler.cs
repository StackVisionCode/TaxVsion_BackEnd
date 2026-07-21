using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Domain.Compensations;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Compensations.CompensateRedemption;

public static class CompensateRedemptionHandler
{
    public static async Task<Result<CompensateRedemptionResponse>> Handle(
        CompensateRedemptionCommand command,
        ICodeDefinitionRepository definitions,
        ICodeReservationRepository reservations,
        ICodeRedemptionRepository redemptions,
        ICodeCompensationRepository compensations,
        ICodeUsageCounterRepository usageCounters,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Failure("Codes.CompensateRedemption.InvalidTenant", "TenantId is required.");

        if (command.RedemptionId == Guid.Empty)
            return Failure("Codes.CompensateRedemption.InvalidRedemption", "RedemptionId is required.");

        if (command.SourceEventId == Guid.Empty)
            return Failure("Codes.CompensateRedemption.InvalidSourceEvent", "SourceEventId is required.");

        var currency = command.Currency ?? string.Empty;
        var reason = command.Reason ?? string.Empty;
        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<CompensateRedemptionResponse>(keyResult.Error);

        var adjustmentResult = Money.Create(command.AdjustmentAmountCents, currency);
        if (adjustmentResult.IsFailure)
            return Result.Failure<CompensateRedemptionResponse>(adjustmentResult.Error);

        var fingerprint = OperationFingerprint.Create(
            command.TenantId,
            command.RedemptionId,
            command.Type,
            command.AdjustmentAmountCents,
            currency.Trim().ToUpperInvariant(),
            reason.Trim(),
            command.SourceEventId
        );

        return await idempotency.ExecuteAsync(
            "Codes.CompensateRedemption.v1",
            command.RedemptionId,
            keyResult.Value,
            fingerprint,
            async operationCt =>
            {
                var redemption = await redemptions.GetByIdAsync(command.TenantId, command.RedemptionId, operationCt);
                if (redemption is null)
                    return Failure("Codes.CompensateRedemption.NotFound", "Redemption was not found.");

                var existingSourceEvent = await compensations.GetBySourceEventIdAsync(
                    command.TenantId,
                    command.RedemptionId,
                    command.SourceEventId,
                    operationCt
                );
                if (existingSourceEvent is not null)
                {
                    if (existingSourceEvent.PayloadFingerprint != fingerprint)
                        return Failure(
                            "Codes.CompensateRedemption.SourceEventConflict",
                            "SourceEventId was already processed with a different payload."
                        );

                    return Result.Success(CompensateRedemptionResponse.From(existingSourceEvent));
                }

                var reservation = await reservations.GetByIdAsync(
                    command.TenantId,
                    redemption.ReservationId,
                    operationCt
                );
                if (reservation is null)
                    return Failure("Codes.CompensateRedemption.ReservationNotFound", "Reservation was not found.");

                var definition = await definitions.GetApplicableByIdAsync(
                    command.TenantId,
                    redemption.CodeDefinitionId,
                    operationCt
                );
                if (definition is null)
                    return Failure("Codes.CompensateRedemption.CodeNotFound", "Code definition was not found.");

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                CodeUsageCounterSet? counterSet = null;
                if (command.Type == CodeCompensationType.RestoreAvailability)
                {
                    var counterSetResult = await CodeUsageCounterSet.LoadAsync(
                        definition,
                        command.TenantId,
                        reservation.Subject,
                        usageCounters,
                        nowUtc,
                        operationCt
                    );
                    if (counterSetResult.IsFailure)
                        return Result.Failure<CompensateRedemptionResponse>(counterSetResult.Error);

                    counterSet = counterSetResult.Value;
                }

                var cumulativeAmount = await compensations.GetCumulativeAdjustmentAmountCentsAsync(
                    command.TenantId,
                    command.RedemptionId,
                    operationCt
                );
                var compensationResult = CodeCompensation.Create(
                    redemption,
                    command.Type,
                    adjustmentResult.Value,
                    cumulativeAmount,
                    reason,
                    command.SourceEventId,
                    keyResult.Value,
                    fingerprint,
                    nowUtc
                );
                if (compensationResult.IsFailure)
                    return Result.Failure<CompensateRedemptionResponse>(compensationResult.Error);

                var reservationResult = reservation.RecordCompensation(
                    compensationResult.Value.Id,
                    compensationResult.Value.IsFinal,
                    nowUtc
                );
                if (reservationResult.IsFailure)
                    return Result.Failure<CompensateRedemptionResponse>(reservationResult.Error);

                if (command.Type == CodeCompensationType.RestoreAvailability)
                {
                    var restoreResult = counterSet!.RestoreAll(definition, nowUtc);
                    if (restoreResult.IsFailure)
                        return Result.Failure<CompensateRedemptionResponse>(restoreResult.Error);
                }

                await compensations.AddAsync(compensationResult.Value, operationCt);
                return Result.Success(CompensateRedemptionResponse.From(compensationResult.Value));
            },
            ct
        );
    }

    private static Result<CompensateRedemptionResponse> Failure(string code, string message) =>
        Result.Failure<CompensateRedemptionResponse>(new Error(code, message));
}
