using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Definitions.CreateCodeDefinition;

public static class CreateCodeDefinitionHandler
{
    public static async Task<Result<CreateCodeDefinitionResponse>> Handle(
        CreateCodeDefinitionCommand command,
        ICodeDefinitionRepository definitions,
        ICodeTokenHasher tokenHasher,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        if (command.OwnerTenantId == Guid.Empty)
            return Failure("Codes.CreateCodeDefinition.InvalidOwner", "OwnerTenantId is required.");

        if (command.ActorUserId == Guid.Empty)
            return Failure("Codes.CreateCodeDefinition.InvalidActor", "ActorUserId is required.");

        if (string.IsNullOrWhiteSpace(command.CodeToken))
            return Failure("Codes.CreateCodeDefinition.InvalidToken", "Code token is required.");

        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<CreateCodeDefinitionResponse>(keyResult.Error);

        var hashResult = tokenHasher.Hash(command.CodeToken);
        if (hashResult.IsFailure)
            return Result.Failure<CreateCodeDefinitionResponse>(hashResult.Error);

        var displayResult = CodeDisplay.FromToken(command.CodeToken);
        if (displayResult.IsFailure)
            return Result.Failure<CreateCodeDefinitionResponse>(displayResult.Error);

        var benefitResult = CreateBenefit(command);
        if (benefitResult.IsFailure)
            return Result.Failure<CreateCodeDefinitionResponse>(benefitResult.Error);

        var minimumPurchaseResult = CreateMinimumPurchase(command);
        if (minimumPurchaseResult.IsFailure)
            return Result.Failure<CreateCodeDefinitionResponse>(minimumPurchaseResult.Error);

        var canonicalScopes = string.Join(
            ",",
            (command.Scopes ?? [])
                .Select(scope => $"{(int)scope.Type}:{(scope.ScopeId ?? string.Empty).Trim()}:{(int)scope.Mode}")
                .Order(StringComparer.Ordinal)
        );
        var fingerprint = OperationFingerprint.Create(
            command.OwnerTenantId,
            command.OwnerScope,
            command.TenantScopeId,
            command.Name?.Trim(),
            command.Kind,
            hashResult.Value.Value,
            command.BenefitType,
            command.PercentageBasisPoints,
            command.FixedAmountCents,
            command.FixedAmountCurrency?.Trim().ToUpperInvariant(),
            command.MinimumPurchaseAmountCents,
            command.MinimumPurchaseCurrency?.Trim().ToUpperInvariant(),
            command.AllowStacking,
            command.StartsAtUtc,
            command.ExpiresAtUtc,
            command.MaxRedemptions,
            command.MaxRedemptionsPerTenant,
            command.MaxRedemptionsPerSubject,
            canonicalScopes,
            command.ActorUserId
        );

        return await idempotency.ExecuteAsync(
            "Codes.CreateCodeDefinition.v1",
            command.OwnerTenantId,
            keyResult.Value,
            fingerprint,
            async operationCt =>
            {
                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var definitionResult = CodeDefinition.Create(
                    command.OwnerTenantId,
                    command.OwnerScope,
                    command.TenantScopeId,
                    command.Name ?? string.Empty,
                    command.Kind,
                    hashResult.Value,
                    displayResult.Value,
                    command.StartsAtUtc,
                    command.ExpiresAtUtc,
                    command.MaxRedemptions,
                    command.MaxRedemptionsPerTenant,
                    command.MaxRedemptionsPerSubject,
                    command.ActorUserId,
                    nowUtc
                );
                if (definitionResult.IsFailure)
                    return Result.Failure<CreateCodeDefinitionResponse>(definitionResult.Error);

                var definition = definitionResult.Value;
                var ruleResult = definition.PublishRuleVersion(
                    benefitResult.Value,
                    minimumPurchaseResult.Value,
                    command.AllowStacking,
                    command.ActorUserId,
                    nowUtc
                );
                if (ruleResult.IsFailure)
                    return Result.Failure<CreateCodeDefinitionResponse>(ruleResult.Error);

                foreach (var scope in command.Scopes ?? [])
                {
                    var scopeResult = definition.AddScope(
                        scope.Type,
                        scope.ScopeId ?? string.Empty,
                        scope.Mode,
                        command.ActorUserId,
                        nowUtc
                    );
                    if (scopeResult.IsFailure)
                        return Result.Failure<CreateCodeDefinitionResponse>(scopeResult.Error);
                }

                await definitions.AddAsync(definition, operationCt);
                return Result.Success(CreateCodeDefinitionResponse.From(definition));
            },
            ct
        );
    }

    private static Result<CodeBenefit> CreateBenefit(CreateCodeDefinitionCommand command)
    {
        if (command.BenefitType == CodeBenefitType.Percentage)
        {
            if (
                command.PercentageBasisPoints is null
                || command.FixedAmountCents is not null
                || !string.IsNullOrWhiteSpace(command.FixedAmountCurrency)
            )
                return Result.Failure<CodeBenefit>(
                    new Error(
                        "Codes.CreateCodeDefinition.InvalidPercentageBenefit",
                        "Percentage benefit requires basis points and cannot include a fixed amount."
                    )
                );

            var percentageResult = Domain.ValueObjects.PercentageBasisPoints.Create(
                command.PercentageBasisPoints.Value
            );
            return percentageResult.IsFailure
                ? Result.Failure<CodeBenefit>(percentageResult.Error)
                : CodeBenefit.CreatePercentage(percentageResult.Value);
        }

        if (command.BenefitType == CodeBenefitType.FixedAmount)
        {
            if (
                command.FixedAmountCents is null
                || string.IsNullOrWhiteSpace(command.FixedAmountCurrency)
                || command.PercentageBasisPoints is not null
            )
                return Result.Failure<CodeBenefit>(
                    new Error(
                        "Codes.CreateCodeDefinition.InvalidFixedBenefit",
                        "Fixed benefit requires an amount and cannot include basis points."
                    )
                );

            var moneyResult = Money.Create(
                command.FixedAmountCents.Value,
                command.FixedAmountCurrency ?? string.Empty
            );
            return moneyResult.IsFailure
                ? Result.Failure<CodeBenefit>(moneyResult.Error)
                : CodeBenefit.CreateFixedAmount(moneyResult.Value);
        }

        return Result.Failure<CodeBenefit>(
            new Error(
                "Codes.CreateCodeDefinition.UnsupportedBenefit",
                "This use case supports only Percentage and FixedAmount benefits."
            )
        );
    }

    private static Result<Money?> CreateMinimumPurchase(CreateCodeDefinitionCommand command)
    {
        if (command.MinimumPurchaseAmountCents is null && command.MinimumPurchaseCurrency is null)
            return Result.Success<Money?>(null);

        if (command.MinimumPurchaseAmountCents is null || string.IsNullOrWhiteSpace(command.MinimumPurchaseCurrency))
            return Result.Failure<Money?>(
                new Error(
                    "Codes.CreateCodeDefinition.InvalidMinimumPurchase",
                    "Minimum purchase amount and currency must be provided together."
                )
            );

        var moneyResult = Money.Create(
            command.MinimumPurchaseAmountCents.Value,
            command.MinimumPurchaseCurrency
        );
        return moneyResult.IsFailure
            ? Result.Failure<Money?>(moneyResult.Error)
            : Result.Success<Money?>(moneyResult.Value);
    }

    private static Result<CreateCodeDefinitionResponse> Failure(string code, string message) =>
        Result.Failure<CreateCodeDefinitionResponse>(new Error(code, message));
}
