namespace TaxVision.Codes.Application.Quotes.CreateSystemQuote;

/// <summary>
/// Quotes the tenant's own active BenefitGift code (e.g. a referral welcome discount) without a
/// plaintext CodeToken — for trusted M2M callers acting on the tenant's behalf (no user ever
/// enters this code, so there is no secret to prove knowledge of). Never resolves regular
/// Discount/Promotion codes; <see cref="Abstractions.ICodeDefinitionRepository.GetActiveBenefitGiftByTenantScopeAsync"/>
/// only ever returns Kind=BenefitGift rows.
/// </summary>
public sealed record CreateSystemQuoteCommand(
    Guid TenantId,
    string OfferOwner,
    string OfferId,
    string OfferVersion,
    long GrossAmountCents,
    string Currency,
    string SnapshotHash,
    string IdempotencyKey,
    int TtlSeconds
);
