using TaxVision.Codes.Domain.Definitions;

namespace TaxVision.Codes.Application.Definitions.GetCodeDetails;

public sealed record CodeRuleDetailsResponse(
    Guid RuleId,
    int Version,
    string BenefitType,
    int? PercentageBasisPoints,
    long? FixedAmountCents,
    string? FixedAmountCurrency,
    string? GrantKey,
    int? DurationDays,
    long? MinimumPurchaseAmountCents,
    string? MinimumPurchaseCurrency,
    bool AllowStacking,
    DateTime PublishedAtUtc
)
{
    public static CodeRuleDetailsResponse From(CodeRuleVersion rule) =>
        new(
            rule.Id,
            rule.Version,
            rule.Benefit.Type.ToString(),
            rule.Benefit.Percentage?.Value,
            rule.Benefit.FixedAmount?.AmountCents,
            rule.Benefit.FixedAmount?.Currency,
            rule.Benefit.GrantKey,
            rule.Benefit.DurationDays,
            rule.MinimumPurchase?.AmountCents,
            rule.MinimumPurchase?.Currency,
            rule.AllowStacking,
            rule.PublishedAtUtc
        );
}
