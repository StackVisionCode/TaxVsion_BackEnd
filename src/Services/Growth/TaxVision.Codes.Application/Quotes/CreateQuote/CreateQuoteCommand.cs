using TaxVision.Codes.Domain.ValueObjects;

namespace TaxVision.Codes.Application.Quotes.CreateQuote;

public sealed record CreateQuoteCommand(
    Guid TenantId,
    string CodeToken,
    SubjectType SubjectType,
    string SubjectId,
    string OfferOwner,
    string OfferId,
    string OfferVersion,
    long GrossAmountCents,
    string Currency,
    string SnapshotHash,
    string IdempotencyKey,
    int TtlSeconds,
    IReadOnlyCollection<CodeScopeTargetInput>? ScopeTargets = null
)
{
    public override string ToString() =>
        $"{nameof(CreateQuoteCommand)} {{ TenantId = {TenantId}, CodeToken = <redacted>, "
        + $"OfferId = {OfferId}, GrossAmountCents = {GrossAmountCents}, Currency = {Currency} }}";
}
