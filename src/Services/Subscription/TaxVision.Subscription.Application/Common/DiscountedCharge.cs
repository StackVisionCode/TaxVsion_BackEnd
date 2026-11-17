using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// Un cobro de la suscripción con el descuento vigente ya aplicado: <see cref="NetCents"/> es lo que se cobra y se
/// firma; <see cref="DiscountCents"/> y <see cref="Source"/> viajan a PaymentApp para el recibo (null sin descuento).
/// </summary>
public sealed record DiscountedCharge(
    long GrossCents,
    long NetCents,
    long? DiscountCents,
    string? Source,
    string? Label = null
)
{
    /// <summary>PaymentApp no cobra montos en cero: el descuento siempre deja al menos el mínimo del proveedor.</summary>
    public const long MinimumChargeCents = 50;

    public static DiscountedCharge Full(long grossCents) => new(grossCents, grossCents, null, null);

    public static DiscountedCharge Of(long grossCents, (long Cents, DiscountSource? Source, string? Label) discount)
    {
        var cents = Math.Min(discount.Cents, Math.Max(0, grossCents - MinimumChargeCents));
        return cents <= 0 || discount.Source is null
            ? Full(grossCents)
            : new DiscountedCharge(grossCents, grossCents - cents, cents, discount.Source.ToString(), discount.Label);
    }

    /// <param name="planCode">Plan del cobro cuando no es el actual (el destino de un upgrade).</param>
    /// <param name="nextCharge">Cobro del cambio de plan: usa el código de un solo cobro pendiente.</param>
    public static DiscountedCharge For(
        TenantSubscription subscription,
        DiscountChargeKind kind,
        long grossCents,
        string currency,
        DateTime chargeDateUtc,
        string? planCode = null,
        bool nextCharge = false
    ) =>
        Of(
            grossCents,
            subscription.DiscountWithSourceFor(kind, grossCents, currency, chargeDateUtc, planCode, nextCharge)
        );

    public static async Task<DiscountedCharge> ForAsync(
        ISubscriptionDiscountReader discounts,
        Guid tenantId,
        DiscountChargeKind kind,
        long grossCents,
        string currency,
        DateTime chargeDateUtc,
        CancellationToken ct
    ) => Of(grossCents, await discounts.ForChargeAsync(tenantId, kind, grossCents, currency, chargeDateUtc, ct));
}
