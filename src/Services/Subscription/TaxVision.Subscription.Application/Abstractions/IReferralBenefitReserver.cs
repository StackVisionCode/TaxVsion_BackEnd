namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>
/// Talks to Growth (M2M) to discover and reserve a tenant's referee welcome-discount code
/// (issued by Growth's Referrals attribution flow) right before its first real charge.
/// Never throws — a discovery/network failure here must not block the charge itself; the
/// tenant simply proceeds at full price and the discount stays available for a later retry.
/// </summary>
public interface IReferralBenefitReserver
{
    Task<ReferralBenefitReservation?> TryReserveAsync(
        Guid tenantId,
        string offerId,
        long grossAmountCents,
        string currency,
        string idempotencyKey,
        CancellationToken ct = default
    );
}

public sealed record ReferralBenefitReservation(
    Guid CodeReservationId,
    Guid PaymentId,
    long GrossAmountCents,
    long DiscountAmountCents,
    long NetAmountCents,
    string Currency,
    string SnapshotHash
);
