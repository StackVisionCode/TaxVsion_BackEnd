using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;
using TaxVision.Growth.SharedKernel.Buyers;

namespace TaxVision.Codes.Application.Quotes.CreateQuote;

public static class CreateQuoteHandler
{
    public static async Task<Result<CreateQuoteResponse>> Handle(
        CreateQuoteCommand command,
        ICodeDefinitionRepository definitions,
        ICodeQuoteRepository quotes,
        ICodeUsageCounterRepository counters,
        ICodeTokenHasher tokenHasher,
        IBuyerIdentityHasher buyerHasher,
        IDisposableEmailDomains disposableDomains,
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

        var buyerResult = BuyerSubject.Resolve(command.BuyerEmail, buyerHasher, disposableDomains);
        if (buyerResult.IsFailure)
            return Result.Failure<CreateQuoteResponse>(buyerResult.Error);

        if (command.Context == CodeUsageContext.ExistingSubscription && command.BuyerTenantId is null)
            return Failure("Codes.CreateQuote.BuyerTenantRequired", "The buying office is required.");

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
            scopeFingerprint,
            buyerResult.Value ?? string.Empty,
            command.Context,
            command.BuyerTenantId,
            string.Join(",", (command.StackedWithCodeDefinitionIds ?? []).Order())
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
                // La oficina y el comprador los pone Growth desde el pedido; los del llamador no cuentan.
                foreach (
                    var input in (command.ScopeTargets ?? []).Where(target =>
                        target.Type is not (CodeScopeType.Tenant or CodeScopeType.Subject)
                    )
                )
                {
                    var targetResult = CodeScopeTarget.Create(input.Type, input.ScopeId);
                    if (targetResult.IsFailure)
                        return Result.Failure<CreateQuoteResponse>(targetResult.Error);

                    targets.Add(targetResult.Value);
                }

                var implicitTargets = ImplicitTargets(command.BuyerTenantId, buyerResult.Value);
                if (implicitTargets.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(implicitTargets.Error);
                targets.AddRange(implicitTargets.Value);

                var definition = await definitions.GetApplicableByHashAsync(
                    command.TenantId,
                    codeHashResult.Value,
                    operationCt
                );
                if (definition is null)
                    return Failure("Codes.CreateQuote.CodeNotFound", "No applicable code was found.");

                var stacked = await StackedWithAsync(definitions, command, operationCt);
                var stackable = definition.EnsureStackableWith(stacked);
                if (stackable.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(stackable.Error);

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
                    nowUtc,
                    command.Context
                );
                if (quoteResult.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(quoteResult.Error);

                if (buyerResult.Value is { } buyerSubjectId)
                {
                    var attached = quoteResult.Value.AttachBuyer(buyerSubjectId);
                    if (attached.IsFailure)
                        return Result.Failure<CreateQuoteResponse>(attached.Error);
                }

                // Quien ya usó el código lo sabe desde la vista previa, no al pagar.
                var available = await CodeUsageCounterSet.EnsureSubjectAvailableAsync(
                    definition,
                    command.TenantId,
                    quoteResult.Value.LimitSubject,
                    counters,
                    operationCt
                );
                if (available.IsFailure)
                    return Result.Failure<CreateQuoteResponse>(available.Error);

                await quotes.AddAsync(quoteResult.Value, operationCt);
                var rule = definition.RuleVersions.Single(item => item.Version == quoteResult.Value.RuleVersion);
                return Result.Success(CreateQuoteResponse.From(quoteResult.Value, rule));
            },
            ct
        );
    }

    private static async Task<IReadOnlyCollection<CodeDefinition>> StackedWithAsync(
        ICodeDefinitionRepository definitions,
        CreateQuoteCommand command,
        CancellationToken ct
    )
    {
        var stacked = new List<CodeDefinition>();
        foreach (var id in (command.StackedWithCodeDefinitionIds ?? []).Distinct())
        {
            if (await definitions.GetApplicableByIdAsync(command.TenantId, id, ct) is { } other)
                stacked.Add(other);
        }

        return stacked;
    }

    private static Result<List<CodeScopeTarget>> ImplicitTargets(Guid? buyerTenantId, string? buyerSubjectId)
    {
        var targets = new List<CodeScopeTarget>();
        if (buyerTenantId is { } tenant)
        {
            var tenantTarget = CodeScopeTarget.Create(CodeScopeType.Tenant, tenant.ToString());
            if (tenantTarget.IsFailure)
                return Result.Failure<List<CodeScopeTarget>>(tenantTarget.Error);
            targets.Add(tenantTarget.Value);
        }

        if (buyerSubjectId is not null)
        {
            var subjectTarget = CodeScopeTarget.Create(CodeScopeType.Subject, buyerSubjectId);
            if (subjectTarget.IsFailure)
                return Result.Failure<List<CodeScopeTarget>>(subjectTarget.Error);
            targets.Add(subjectTarget.Value);
        }

        return Result.Success(targets);
    }

    private static Result<CreateQuoteResponse> Failure(string code, string message) =>
        Result.Failure<CreateQuoteResponse>(new Error(code, message));
}
