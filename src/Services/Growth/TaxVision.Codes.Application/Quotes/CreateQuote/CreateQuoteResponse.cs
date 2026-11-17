using TaxVision.Codes.Domain.Definitions;
using TaxVision.Codes.Domain.Quotes;

namespace TaxVision.Codes.Application.Quotes.CreateQuote;

public sealed record CreateQuoteResponse(
    Guid QuoteId,
    Guid CodeDefinitionId,
    int RuleVersion,
    long GrossAmountCents,
    long DiscountAmountCents,
    long NetAmountCents,
    string Currency,
    string SnapshotHash,
    DateTime ExpiresAtUtc,
    // El beneficio y su duración, para mostrarlo y guardarlo en la suscripción.
    CodeBenefitType BenefitType = CodeBenefitType.Percentage,
    int? PercentageBasisPoints = null,
    long? FixedAmountCents = null,
    CodeDurationKind Duration = CodeDurationKind.Once,
    int? DurationMonths = null,
    // El código enmascarado (PREFIJO...ÚLTIMOS4) para el recibo; nunca el código en claro.
    string? CodeDisplay = null
)
{
    public static CreateQuoteResponse From(CodeQuote quote, CodeRuleVersion rule) =>
        From(quote) with
        {
            BenefitType = rule.Benefit.Type,
            PercentageBasisPoints = rule.Benefit.Percentage?.Value,
            FixedAmountCents = rule.Benefit.FixedAmount?.AmountCents,
            Duration = rule.Duration.Kind,
            DurationMonths = rule.Duration.Months,
        };

    public static CreateQuoteResponse From(CodeQuote quote) =>
        new(
            quote.Id,
            quote.CodeDefinitionId,
            quote.RuleVersion,
            quote.GrossAmount.AmountCents,
            quote.DiscountAmount.AmountCents,
            quote.NetAmount.AmountCents,
            quote.NetAmount.Currency,
            quote.SnapshotHash.Value,
            quote.ExpiresAtUtc,
            CodeDisplay: quote.CodeDisplay.ToString()
        );
}
