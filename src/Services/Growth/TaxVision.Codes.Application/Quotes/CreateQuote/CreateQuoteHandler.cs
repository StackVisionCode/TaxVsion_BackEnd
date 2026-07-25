using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Quotes.CreateQuote;

public static class CreateQuoteHandler
{
    public static async Task<Result<CreateQuoteResponse>> Handle(
        CreateQuoteCommand command,
        ICodeDefinitionRepository definitions,
        ICodeQuoteRepository quotes,
        ICodeTokenHasher tokenHasher,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.TenantId == Guid.Empty)
            return Failure("Codes.CreateQuote.InvalidTenant", "TenantId is required.");

        if (string.IsNullOrWhiteSpace(command.CodeToken))
            return Failure("Codes.CreateQuote.InvalidCodeToken", "Code token is required.");

        var currency = command.Currency ?? string.Empty;
        var subjectId = command.SubjectId ?? string.Empty;
        var offerOwner = command.OfferOwner ?? string.Empty;
        var offerId = command.OfferId ?? string.Empty;
        var offerVersion = command.OfferVersion ?? string.Empty;
        var snapshotHash = command.SnapshotHash ?? string.Empty;

        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<CreateQuoteResponse>(keyResult.Error);

        var codeHashResult = tokenHasher.Hash(command.CodeToken);
        if (codeHashResult.IsFailure)
            return Result.Failure<CreateQuoteResponse>(codeHashResult.Error);

        var scopeFingerprint = string.Join(
            ",",
            (command.ScopeTargets ?? [])
                .Select(target => $"{(int)target.Type}:{(target.ScopeId ?? string.Empty).Trim()}")
                .Order(StringComparer.Ordinal)
        );
        var fingerprint = OperationFingerprint.Create(
            command.TenantId,
            codeHashResult.Value.Value,
            command.SubjectType,
            subjectId.Trim(),
            offerOwner.Trim(),
            offerId.Trim(),
            offerVersion.Trim(),
            command.GrossAmountCents,
            currency.Trim().ToUpperInvariant(),
            snapshotHash.Trim().ToLowerInvariant(),
            command.TtlSeconds,
            scopeFingerprint
        );

        return await idempotency.ExecuteAsync(
            command.TenantId,
            "Codes.CreateQuote.v1",
            command.TenantId,
            keyResult.Value,
            fingerprint,
            async operationCt =>
            {
                var grossResult = Money.Create(command.GrossAmountCents, currency);
                if (grossResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(grossResult.Error);

                var subjectResult = SubjectReference.Create(command.SubjectType, subjectId);
                if (subjectResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(subjectResult.Error);

                var offerResult = OfferReference.Create(offerOwner, offerId, offerVersion);
                if (offerResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(offerResult.Error);

                var snapshotResult = SnapshotHash.Create(snapshotHash);
                if (snapshotResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(snapshotResult.Error);

                var targets = new List<CodeScopeTarget>();
                foreach (var input in command.ScopeTargets ?? [])
                {
                    var targetResult = CodeScopeTarget.Create(input.Type, input.ScopeId);
                    if (targetResult.IsFailure)
                        return Result.Failure<CreateQuoteResponse>(targetResult.Error);

                    targets.Add(targetResult.Value);
                }

                var definition = await definitions.GetApplicableByHashAsync(
                    command.TenantId,
                    codeHashResult.Value,
                    operationCt
                );
                if (definition is null)
                    return Failure("Codes.CreateQuote.CodeNotFound", "No applicable code was found.");

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var quoteResult = definition.CreateQuote(
                    command.TenantId,
                    subjectResult.Value,
                    offerResult.Value,
                    targets,
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
