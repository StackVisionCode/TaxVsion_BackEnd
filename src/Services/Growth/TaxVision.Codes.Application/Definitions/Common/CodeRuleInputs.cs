using BuildingBlocks.Results;
using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Definitions.Common;

/// <summary>Arma el beneficio y la compra mínima de una regla; lo comparten el alta y la nueva versión.</summary>
public static class CodeRuleInputs
{
    public static Result<CodeBenefit> Benefit(
        string errorPrefix,
        CodeBenefitType type,
        int? percentageBasisPoints,
        long? fixedAmountCents,
        string? fixedAmountCurrency
    )
    {
        if (type == CodeBenefitType.Percentage)
        {
            if (
                percentageBasisPoints is null
                || fixedAmountCents is not null
                || !string.IsNullOrWhiteSpace(fixedAmountCurrency)
            )
                return Result.Failure<CodeBenefit>(
                    new Error(
                        $"{errorPrefix}.InvalidPercentageBenefit",
                        "Percentage benefit requires basis points and cannot include a fixed amount."
                    )
                );

            var percentageResult = PercentageBasisPoints.Create(percentageBasisPoints.Value);
            return percentageResult.IsFailure
                ? Result.Failure<CodeBenefit>(percentageResult.Error)
                : CodeBenefit.CreatePercentage(percentageResult.Value);
        }

        if (type == CodeBenefitType.FixedAmount)
        {
            if (
                fixedAmountCents is null
                || string.IsNullOrWhiteSpace(fixedAmountCurrency)
                || percentageBasisPoints is not null
            )
                return Result.Failure<CodeBenefit>(
                    new Error(
                        $"{errorPrefix}.InvalidFixedBenefit",
                        "Fixed benefit requires an amount and cannot include basis points."
                    )
                );

            var moneyResult = Money.Create(fixedAmountCents.Value, fixedAmountCurrency);
            return moneyResult.IsFailure
                ? Result.Failure<CodeBenefit>(moneyResult.Error)
                : CodeBenefit.CreateFixedAmount(moneyResult.Value);
        }

        return Result.Failure<CodeBenefit>(
            new Error(
                $"{errorPrefix}.UnsupportedBenefit",
                "This use case supports only Percentage and FixedAmount benefits."
            )
        );
    }

    public static Result<Money?> MinimumPurchase(string errorPrefix, long? amountCents, string? currency)
    {
        if (amountCents is null && currency is null)
            return Result.Success<Money?>(null);

        if (amountCents is null || string.IsNullOrWhiteSpace(currency))
            return Result.Failure<Money?>(
                new Error(
                    $"{errorPrefix}.InvalidMinimumPurchase",
                    "Minimum purchase amount and currency must be provided together."
                )
            );

        var moneyResult = Money.Create(amountCents.Value, currency);
        return moneyResult.IsFailure
            ? Result.Failure<Money?>(moneyResult.Error)
            : Result.Success<Money?>(moneyResult.Value);
    }
}
