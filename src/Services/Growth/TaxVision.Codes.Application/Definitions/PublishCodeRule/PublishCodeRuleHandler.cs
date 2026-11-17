using BuildingBlocks.Results;
using TaxVision.Codes.Application.Abstractions;
using TaxVision.Codes.Application.Common;
using TaxVision.Codes.Application.Definitions.Common;
using TaxVision.Codes.Application.Definitions.GetCodeDetails;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Definitions.PublishCodeRule;

public static class PublishCodeRuleHandler
{
    private const string Prefix = "Codes.PublishCodeRule";

    public static async Task<Result<CodeDefinitionDetailsResponse>> Handle(
        PublishCodeRuleCommand command,
        ICodeDefinitionRepository definitions,
        IBusinessIdempotencyExecutor idempotency,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var inputs = Inputs(command);
        if (inputs.IsFailure)
            return Result.Failure<CodeDefinitionDetailsResponse>(inputs.Error);

        var (key, benefit, minimumPurchase, duration) = inputs.Value;
        return await idempotency.ExecuteAsync(
            command.OwnerTenantId,
            "Codes.PublishCodeRule.v1",
            command.CodeDefinitionId,
            key,
            Fingerprint(command),
            async operationCt =>
            {
                var definition = await definitions.GetOwnedByIdAsync(
                    command.OwnerTenantId,
                    command.CodeDefinitionId,
                    operationCt
                );
                if (definition is null || definition.TenantId != command.OwnerTenantId)
                    return Failure("Codes.PublishCodeRule.NotFound", "Owned code definition was not found.");

                var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
                var published = definition.PublishRuleVersion(
                    benefit,
                    minimumPurchase,
                    command.AllowStacking,
                    command.ActorUserId,
                    nowUtc,
                    duration
                );
                return published.IsFailure
                    ? Result.Failure<CodeDefinitionDetailsResponse>(published.Error)
                    : Result.Success(CodeDefinitionDetailsResponse.From(definition));
            },
            ct
        );
    }

    private static Result<(IdempotencyKey, CodeBenefit, Money?, CodeDuration)> Inputs(PublishCodeRuleCommand command)
    {
        if (command.OwnerTenantId == Guid.Empty || command.CodeDefinitionId == Guid.Empty)
            return Invalid(
                "Codes.PublishCodeRule.InvalidDefinition",
                "OwnerTenantId and CodeDefinitionId are required."
            );

        if (command.ActorUserId == Guid.Empty)
            return Invalid("Codes.PublishCodeRule.InvalidActor", "ActorUserId is required.");

        var key = IdempotencyKey.Create(command.IdempotencyKey);
        if (key.IsFailure)
            return Result.Failure<(IdempotencyKey, CodeBenefit, Money?, CodeDuration)>(key.Error);

        var benefit = CodeRuleInputs.Benefit(
            Prefix,
            command.BenefitType,
            command.PercentageBasisPoints,
            command.FixedAmountCents,
            command.FixedAmountCurrency
        );
        if (benefit.IsFailure)
            return Result.Failure<(IdempotencyKey, CodeBenefit, Money?, CodeDuration)>(benefit.Error);

        var minimum = CodeRuleInputs.MinimumPurchase(
            Prefix,
            command.MinimumPurchaseAmountCents,
            command.MinimumPurchaseCurrency
        );
        if (minimum.IsFailure)
            return Result.Failure<(IdempotencyKey, CodeBenefit, Money?, CodeDuration)>(minimum.Error);

        var duration = CodeDuration.Create(command.Duration, command.DurationMonths);
        return duration.IsFailure
            ? Result.Failure<(IdempotencyKey, CodeBenefit, Money?, CodeDuration)>(duration.Error)
            : Result.Success((key.Value, benefit.Value, minimum.Value, duration.Value));
    }

    private static PayloadFingerprint Fingerprint(PublishCodeRuleCommand command) =>
        OperationFingerprint.Create(
            command.OwnerTenantId,
            command.CodeDefinitionId,
            command.BenefitType,
            command.PercentageBasisPoints,
            command.FixedAmountCents,
            command.FixedAmountCurrency?.Trim().ToUpperInvariant(),
            command.MinimumPurchaseAmountCents,
            command.MinimumPurchaseCurrency?.Trim().ToUpperInvariant(),
            command.AllowStacking,
            command.Duration,
            command.DurationMonths,
            command.ActorUserId
        );

    private static Result<(IdempotencyKey, CodeBenefit, Money?, CodeDuration)> Invalid(string code, string message) =>
        Result.Failure<(IdempotencyKey, CodeBenefit, Money?, CodeDuration)>(new Error(code, message));

    private static Result<CodeDefinitionDetailsResponse> Failure(string code, string message) =>
        Result.Failure<CodeDefinitionDetailsResponse>(new Error(code, message));
}
