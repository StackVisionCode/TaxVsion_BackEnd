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
    DateTime ExpiresAtUtc
)
{
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
            quote.ExpiresAtUtc
        );
}
