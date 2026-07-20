using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Domain.Definitions;

public sealed class CodeRuleVersion : TenantEntity
{
    public Guid CodeDefinitionId { get; private set; }
    public int Version { get; private set; }
    public CodeBenefit Benefit { get; private set; } = null!;
    public Money? MinimumPurchase { get; private set; }
    public bool AllowStacking { get; private set; }
    public DateTime PublishedAtUtc { get; private set; }
    public Guid PublishedBy { get; private set; }

    private CodeRuleVersion() { }

    internal static Result<CodeRuleVersion> Create(
        Guid tenantId,
        Guid codeDefinitionId,
        int version,
        CodeBenefit benefit,
        Money? minimumPurchase,
        bool allowStacking,
        Guid publishedBy,
        DateTime publishedAtUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<CodeRuleVersion>(
                new Error("Codes.CodeRuleVersion.InvalidTenant", "TenantId is required.")
            );

        if (codeDefinitionId == Guid.Empty)
            return Result.Failure<CodeRuleVersion>(
                new Error("Codes.CodeRuleVersion.InvalidCodeDefinition", "CodeDefinitionId is required.")
            );

        if (version <= 0)
            return Result.Failure<CodeRuleVersion>(
                new Error("Codes.CodeRuleVersion.InvalidVersion", "Version must be greater than zero.")
            );

        if (minimumPurchase is { AmountCents: <= 0 })
            return Result.Failure<CodeRuleVersion>(
                new Error(
                    "Codes.CodeRuleVersion.InvalidMinimumPurchase",
                    "Minimum purchase must be greater than zero when provided."
                )
            );

        if (
            minimumPurchase is not null
            && benefit.FixedAmount is not null
            && !string.Equals(minimumPurchase.Currency, benefit.FixedAmount.Currency, StringComparison.Ordinal)
        )
            return Result.Failure<CodeRuleVersion>(
                new Error(
                    "Codes.CodeRuleVersion.CurrencyMismatch",
                    "Minimum purchase and fixed discount must use the same currency."
                )
            );

        if (publishedBy == Guid.Empty)
            return Result.Failure<CodeRuleVersion>(
                new Error("Codes.CodeRuleVersion.InvalidActor", "PublishedBy is required.")
            );

        var rule = new CodeRuleVersion
            {
                CodeDefinitionId = codeDefinitionId,
                Version = version,
                Benefit = benefit,
                MinimumPurchase = minimumPurchase,
                AllowStacking = allowStacking,
                PublishedBy = publishedBy,
                PublishedAtUtc = publishedAtUtc,
            };
        rule.SetTenant(tenantId);
        return Result.Success(rule);
    }

    public Result<Money> EvaluateDiscount(Money grossAmount)
    {
        if (MinimumPurchase is not null)
        {
            if (!string.Equals(MinimumPurchase.Currency, grossAmount.Currency, StringComparison.Ordinal))
                return Result.Failure<Money>(
                    new Error(
                        "Codes.CodeRuleVersion.CurrencyMismatch",
                        "Gross amount currency does not match the rule currency."
                    )
                );

            if (grossAmount.AmountCents < MinimumPurchase.AmountCents)
                return Result.Failure<Money>(
                    new Error(
                        "Codes.CodeRuleVersion.MinimumPurchaseNotMet",
                        "Gross amount does not meet the minimum purchase."
                    )
                );
        }

        return Benefit.CalculateDiscount(grossAmount);
    }
}
