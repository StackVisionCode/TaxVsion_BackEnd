using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Infrastructure.Persistence.Migrations;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// D-N50: el relleno de la migración marca solo los de N meses vigentes que terminan en los próximos 7 días. Corre el
/// SQL de la migración en la base temporal de las T-INT.
/// </summary>
public sealed class DiscountEndingNoticeBackfillIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _replaced = new();
    private readonly SubscriptionIntegrationDb _endingSoon = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _replaced.CleanupAsync();
        await _endingSoon.CleanupAsync();
    }

    [Fact]
    public async Task Backfill_marks_only_active_months_discounts_ending_within_seven_days()
    {
        var nowUtc = DateTime.UtcNow;
        // Oficina 1: uno que terminaba en 3 días fue reemplazado por otro que termina en 20.
        var replacedSoon = await GrantAsync(_replaced, endsInDays: 3, replace: false);
        var later = await GrantAsync(_replaced, endsInDays: 20, replace: true);
        // Oficina 2: vigente y termina en 3 días.
        var soon = await GrantAsync(_endingSoon, endsInDays: 3, replace: false);

        await using (var db = _endingSoon.NewContext())
            await db.Database.ExecuteSqlRawAsync(BackfillDiscountEndingNotices.Sql);

        Assert.NotNull(await NoticeSentAtAsync(soon));
        Assert.Null(await NoticeSentAtAsync(replacedSoon));
        Assert.Null(await NoticeSentAtAsync(later));
        Assert.True(await NoticeSentAtAsync(soon) >= nowUtc.AddMinutes(-1));
    }

    // Un mes que empezó hace (un mes - N días): termina en N días.
    private static async Task<Guid> GrantAsync(SubscriptionIntegrationDb office, int endsInDays, bool replace)
    {
        if (!replace)
            await office.SeedActiveSubscriptionAsync();
        var nowUtc = DateTime.UtcNow;
        await using var db = office.NewContext();
        var subscription = await db
            .Subscriptions.IgnoreQueryFilters()
            .Include(s => s.Discounts)
            .SingleAsync(s => s.TenantId == office.TenantId);
        var terms = SubscriptionDiscountTerms
            .Create(
                DiscountSource.Admin,
                null,
                2_000,
                null,
                null,
                DiscountDuration.Months,
                1,
                DiscountAppliesTo.Base,
                null,
                "Retention deal",
                nowUtc.AddDays(endsInDays).AddMonths(-1)
            )
            .Value;
        var discount = subscription.GrantDiscount(terms, replace, Guid.Empty, nowUtc).Value;
        await db.SaveChangesAsync();
        return discount.Id;
    }

    private async Task<DateTime?> NoticeSentAtAsync(Guid discountId)
    {
        await using var db = _endingSoon.NewContext();
        return await db.Set<SubscriptionDiscount>()
            .IgnoreQueryFilters()
            .Where(discount => discount.Id == discountId)
            .Select(discount => discount.EndingNoticeSentAtUtc)
            .SingleAsync();
    }
}
