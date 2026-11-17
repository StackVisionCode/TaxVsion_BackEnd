using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Tests.TestDoubles;

/// <summary>Lector de descuentos para pruebas que no los usan: ningún cobro lleva descuento.</summary>
internal sealed class NoSubscriptionDiscounts : ISubscriptionDiscountReader
{
    public Task<(long Cents, DiscountSource? Source, string? Label)> ForChargeAsync(
        Guid tenantId,
        DiscountChargeKind kind,
        long grossCents,
        string currency,
        DateTime chargeDateUtc,
        CancellationToken ct = default
    ) => Task.FromResult<(long, DiscountSource?, string?)>((0, null, null));
}
