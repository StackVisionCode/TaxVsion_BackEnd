using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Application.Quotes.CreateQuote;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Quotes.CreateSystemQuote;

public static class CreateSystemQuoteHandler
{
    public static async Task<Result<CreateQuoteResponse>> Handle(
        CreateSystemQuoteCommand command,
        ICodeDefinitionRepository definitions,
        ICodeQuoteRepository quotes,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Failure("Codes.CreateSystemQuote.InvalidTenant", "TenantId is required.");

        var currency = command.Currency ?? string.Empty;
        var offerOwner = command.OfferOwner ?? string.Empty;
        var offerId = command.OfferId ?? string.Empty;
        var offerVersion = command.OfferVersion ?? string.Empty;
        var snapshotHash = command.SnapshotHash ?? string.Empty;

        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<CreateQuoteResponse>(keyResult.Error);

        var fingerprint = OperationFingerprint.Create(
            command.TenantId,
            "BenefitGift",
            offerOwner.Trim(),
            offerId.Trim(),
            offerVersion.Trim(),
            command.GrossAmountCents,
            currency.Trim().ToUpperInvariant(),
            snapshotHash.Trim().ToLowerInvariant(),
            command.TtlSeconds
        );

        return await idempotency.ExecuteAsync(
            command.TenantId,
            "Codes.CreateSystemQuote.v1",
            command.TenantId,
            keyResult.Value,
            fingerprint,
            async operationCt =>
            {
                var grossResult = Money.Create(command.GrossAmountCents, currency);
                if (grossResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(grossResult.Error);

                var subjectResult = SubjectReference.Create(SubjectType.Tenant, command.TenantId.ToString("D"));
                if (subjectResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(subjectResult.Error);

                var offerResult = OfferReference.Create(offerOwner, offerId, offerVersion);
                if (offerResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(offerResult.Error);

                var snapshotResult = SnapshotHash.Create(snapshotHash);
                if (snapshotResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(snapshotResult.Error);

                var definition = await definitions.GetActiveBenefitGiftByTenantScopeAsync(
                    command.TenantId,
                    operationCt
                );
                if (definition is null)
                    return Failure(
                        "Codes.CreateSystemQuote.NoActiveBenefit",
                        "No active benefit-gift code exists for this tenant."
                    );

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var quoteResult = definition.CreateQuote(
                    command.TenantId,
                    subjectResult.Value,
                    offerResult.Value,
                    [],
                    grossResult.Value,
                    snapshotResult.Value,
                    keyResult.Value,
                    fingerprint,
                    TimeSpan.FromSeconds(command.TtlSeconds),
                    nowUtc
                );
                if (quoteResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(quoteResult.Error);

                await quotes.AddAsync(quoteResult.Value, operationCt);
                return Result.Success(CreateQuoteResponse.From(quoteResult.Value));
            },
            ct
        );
    }

    private static Result<CreateQuoteResponse> Failure(string code, string message) =>
        Result.Failure<CreateQuoteResponse>(new Error(code, message));
}
