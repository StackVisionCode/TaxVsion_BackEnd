using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Definitions;

public sealed class CodeBenefit
{
    public CodeBenefitType Type { get; private set; }
    public PercentageBasisPoints? Percentage { get; private set; }
    public Money? FixedAmount { get; private set; }
    public string? GrantKey { get; private set; }
    public int? DurationDays { get; private set; }

    private CodeBenefit() { }

    public static Result<CodeBenefit> CreatePercentage(PercentageBasisPoints percentage) =>
        Result.Success(new CodeBenefit { Type = CodeBenefitType.Percentage, Percentage = percentage });

    public static Result<CodeBenefit> CreateFixedAmount(Money fixedAmount)
    {
        if (fixedAmount.AmountCents <= 0)
            return Result.Failure<CodeBenefit>(
                new Error("Codes.CodeBenefit.InvalidFixedAmount", "Fixed discount must be greater than zero.")
            );

        return Result.Success(new CodeBenefit { Type = CodeBenefitType.FixedAmount, FixedAmount = fixedAmount });
    }

    public static Result<CodeBenefit> CreateGrant(CodeBenefitType type, string grantKey, int? durationDays = null)
    {
        if (
            type
            is not (
                CodeBenefitType.BenefitGift
                or CodeBenefitType.PrelaunchGrant
                or CodeBenefitType.TrialExtension
                or CodeBenefitType.FeatureUnlock
            )
        )
            return Result.Failure<CodeBenefit>(
                new Error("Codes.CodeBenefit.InvalidGrantType", "Benefit type is not a non-monetary grant.")
            );

        if (string.IsNullOrWhiteSpace(grantKey) || grantKey.Trim().Length > 200)
            return Result.Failure<CodeBenefit>(
                new Error("Codes.CodeBenefit.InvalidGrantKey", "GrantKey is required and cannot exceed 200 characters.")
            );

        if (durationDays is <= 0)
            return Result.Failure<CodeBenefit>(
                new Error("Codes.CodeBenefit.InvalidDuration", "DurationDays must be greater than zero when provided.")
            );

        return Result.Success(
            new CodeBenefit
            {
                Type = type,
                GrantKey = grantKey.Trim(),
                DurationDays = durationDays,
            }
        );
    }

    public Result<Money> CalculateDiscount(Money grossAmount)
    {
        if (Type == CodeBenefitType.Percentage)
            return Percentage!.ApplyTo(grossAmount);

        if (Type == CodeBenefitType.FixedAmount)
        {
            if (!string.Equals(FixedAmount!.Currency, grossAmount.Currency, StringComparison.Ordinal))
                return Result.Failure<Money>(
                    new Error(
                        "Codes.CodeBenefit.CurrencyMismatch",
                        "Fixed discount currency must match the gross amount currency."
                    )
                );

            return FixedAmount.Min(grossAmount);
        }

        return Money.Zero(grossAmount.Currency);
    }
}
