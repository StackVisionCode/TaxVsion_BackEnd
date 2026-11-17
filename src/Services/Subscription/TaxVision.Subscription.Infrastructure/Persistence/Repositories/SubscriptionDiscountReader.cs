using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionDiscountReader(SubscriptionDbContext db) : ISubscriptionDiscountReader
{
    public async Task<(long Cents, DiscountSource? Source, string? Label)> ForChargeAsync(
        Guid tenantId,
        DiscountChargeKind kind,
        long grossCents,
        string currency,
        DateTime chargeDateUtc,
        CancellationToken ct = default
    )
    {
        var open = await db.Set<SubscriptionDiscount>()
            .AsNoTracking()
            .Where(discount => discount.TenantId == tenantId && discount.EndedAtUtc == null)
            .ToListAsync(ct);

        foreach (var discount in open)
        {
            var cents = discount.AmountFor(kind, grossCents, currency, chargeDateUtc);
            if (cents > 0)
                return (cents, discount.Source, discount.ReceiptLabel());
        }

        return (0, null, null);
    }
}
