using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Application.Definitions.Common;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Definitions.CreateCodeDefinition;

public static class CreateCodeDefinitionHandler
{
    private const string Prefix = "Codes.CreateCodeDefinition";

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

        var benefitResult = CodeRuleInputs.Benefit(
            Prefix,
            command.BenefitType,
            command.PercentageBasisPoints,
            command.FixedAmountCents,
            command.FixedAmountCurrency
        );
        if (benefitResult.IsFailure)
            return Result.Failure<CreateCodeDefinitionResponse>(benefitResult.Error);

        var minimumPurchaseResult = CodeRuleInputs.MinimumPurchase(
            Prefix,
            command.MinimumPurchaseAmountCents,
            command.MinimumPurchaseCurrency
        );
        if (minimumPurchaseResult.IsFailure)
            return Result.Failure<CreateCodeDefinitionResponse>(minimumPurchaseResult.Error);

        var durationResult = CodeDuration.Create(command.Duration, command.DurationMonths);
        if (durationResult.IsFailure)
            return Result.Failure<CreateCodeDefinitionResponse>(durationResult.Error);

        var scopes = ScopesWithRestriction(command);

        var expiresAtUtc = command.ExpiresOn is { } day ? CommercialTimeZone.EndOfDayUtc(day) : command.ExpiresAtUtc;
        var canonicalScopes = string.Join(
            ",",
            scopes
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
            expiresAtUtc,
            command.MaxRedemptions,
            command.MaxRedemptionsPerTenant,
            command.MaxRedemptionsPerSubject,
            canonicalScopes,
            command.ActorUserId,
            command.Duration,
            command.DurationMonths,
            command.UsableOn
        );

        return await idempotency.ExecuteAsync(
            command.OwnerTenantId,
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
                    expiresAtUtc,
                    command.MaxRedemptions,
                    command.MaxRedemptionsPerTenant,
                    command.MaxRedemptionsPerSubject,
                    command.ActorUserId,
                    nowUtc,
                    command.UsableOn
                );
                if (definitionResult.IsFailure)
                    return Result.Failure<CreateCodeDefinitionResponse>(definitionResult.Error);

                var definition = definitionResult.Value;
                var ruleResult = definition.PublishRuleVersion(
                    benefitResult.Value,
                    minimumPurchaseResult.Value,
                    command.AllowStacking,
                    command.ActorUserId,
                    nowUtc,
                    durationResult.Value
                );
                if (ruleResult.IsFailure)
                    return Result.Failure<CreateCodeDefinitionResponse>(ruleResult.Error);

                foreach (var scope in scopes)
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

    // RestrictToTenantId es un atajo para un alcance Include de esa oficina.
    private static List<CreateCodeScopeInput> ScopesWithRestriction(CreateCodeDefinitionCommand command)
    {
        var scopes = (command.Scopes ?? []).ToList();
        if (command.RestrictToTenantId is { } tenant)
            scopes.Add(new CreateCodeScopeInput(CodeScopeType.Tenant, tenant.ToString(), CodeScopeMode.Include));
        return scopes;
    }

    private static Result<CreateCodeDefinitionResponse> Failure(string code, string message) =>
        Result.Failure<CreateCodeDefinitionResponse>(new Error(code, message));
}
