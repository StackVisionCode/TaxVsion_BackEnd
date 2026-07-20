using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Domain.Reservations;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Reservations.ReserveCode;

public static class ReserveCodeHandler
{
    public static async Task<Result<ReserveCodeResponse>> Handle(
        ReserveCodeCommand command,
        ICodeDefinitionRepository definitions,
        ICodeQuoteRepository quotes,
        ICodeReservationRepository reservations,
        ICodeUsageCounterRepository usageCounters,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Failure("Codes.ReserveCode.InvalidTenant", "TenantId is required.");

        if (command.QuoteId == Guid.Empty)
            return Failure("Codes.ReserveCode.InvalidQuote", "QuoteId is required.");

        if (command.TtlSeconds <= 0)
            return Failure("Codes.ReserveCode.InvalidTtl", "TtlSeconds must be greater than zero.");

        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<ReserveCodeResponse>(keyResult.Error);

        var paymentResult = PaymentReference.Create(command.PaymentSource, command.PaymentId);
        if (paymentResult.IsFailure)
            return Result.Failure<ReserveCodeResponse>(paymentResult.Error);

        var fingerprint = OperationFingerprint.Create(
            command.TenantId,
            command.QuoteId,
            paymentResult.Value.Source,
            paymentResult.Value.PaymentId,
            command.TtlSeconds
        );
        return await idempotency.ExecuteAsync(
            "Codes.ReserveCode.v1",
            command.TenantId,
            keyResult.Value,
            fingerprint,
            async operationCt =>
            {
                var quote = await quotes.GetByIdAsync(command.TenantId, command.QuoteId, operationCt);
                if (quote is null)
                    return Failure("Codes.ReserveCode.QuoteNotFound", "Quote was not found.");

                var definition = await definitions.GetApplicableByIdAsync(
                    command.TenantId,
                    quote.CodeDefinitionId,
                    operationCt
                );
                if (definition is null)
                    return Failure(
                        "Codes.ReserveCode.CodeNotFound",
                        "Code definition was not found or is not applicable."
                    );

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var counterSetResult = await CodeUsageCounterSet.LoadAsync(
                    definition,
                    command.TenantId,
                    quote.Subject,
                    usageCounters,
                    nowUtc,
                    operationCt
                );
                if (counterSetResult.IsFailure)
                    return Result.Failure<ReserveCodeResponse>(counterSetResult.Error);

                var reserveUseResult = counterSetResult.Value.ReserveAll(definition, nowUtc);
                if (reserveUseResult.IsFailure)
                    return Result.Failure<ReserveCodeResponse>(reserveUseResult.Error);

                var requestedExpiry = nowUtc.AddSeconds(command.TtlSeconds);
                var expiresAtUtc = requestedExpiry < quote.ExpiresAtUtc ? requestedExpiry : quote.ExpiresAtUtc;
                var reservationResult = CodeReservation.Create(
                    quote,
                    paymentResult.Value,
                    keyResult.Value,
                    fingerprint,
                    expiresAtUtc,
                    nowUtc
                );
                if (reservationResult.IsFailure)
                    return Result.Failure<ReserveCodeResponse>(reservationResult.Error);

                await reservations.AddAsync(reservationResult.Value, operationCt);
                return Result.Success(ReserveCodeResponse.From(reservationResult.Value));
            },
            ct
        );
    }

    private static Result<ReserveCodeResponse> Failure(string code, string message) =>
        Result.Failure<ReserveCodeResponse>(new Error(code, message));
}
