namespace TaxVision.Subscription.Domain.ValueObjects;

/// <summary>Cargo del período parcial inicial de una compra: lo que PaymentApp debe cobrar.</summary>
public sealed record InitialCharge(string IdempotencyKey, long AmountCents, string Currency)
{
    public static InitialCharge From(string idempotencyKey, Money prorated) =>
        new(
            idempotencyKey,
            (long)Math.Round(prorated.Amount * 100m, MidpointRounding.AwayFromZero),
            prorated.Currency
        );
}
