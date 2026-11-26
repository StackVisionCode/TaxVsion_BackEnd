using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>
/// Contra SQL Server: una oficina con el cobro retenido no aparece en las búsquedas de los jobs que cobran
/// (base, asientos, add-ons, aviso de renovación, suspensión por vencida). Al liberarla vuelve a aparecer.
/// </summary>
public sealed class AdministrativeBillingHoldIntegrationTests : IAsyncLifetime
{
    private const int AllRows = 100_000;
    private readonly SubscriptionIntegrationDb _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.CleanupAsync();

    [Fact]
    public async Task A_held_office_is_left_out_of_the_charging_queries_until_it_is_released()
    {
        await _db.SeedActiveSubscriptionAsync();
        var later = DateTime.UtcNow.AddDays(40);

        Assert.True(await IsDueAsync(later));

        await ApplyStatusAsync(isActive: false, version: 1);

        Assert.False(await IsDueAsync(later));
        await using (var db = _db.NewContext())
        {
            var subscriptions = new TenantSubscriptionRepository(db);
            var renewing = await subscriptions.GetRenewingBetweenAsync(DateTime.UtcNow, later, AllRows);
            var overdue = await subscriptions.GetActiveWithPeriodEndedBeforeAsync(later, AllRows);
            Assert.DoesNotContain(renewing, s => s.TenantId == _db.TenantId);
            Assert.DoesNotContain(overdue, s => s.TenantId == _db.TenantId);

            // Las búsquedas de asientos y add-ons filtran por la retención en SQL: deben traducirse y correr.
            await new SubscriptionSeatRepository(db).GetDueForRenewalAsync(later, 10);
            await new TenantAddOnRepository(db).GetDueForRenewalAsync(later, 10);
        }

        await ApplyStatusAsync(isActive: true, version: 2);

        Assert.True(await IsDueAsync(later));
    }

    private async Task<bool> IsDueAsync(DateTime nowUtc)
    {
        await using var db = _db.NewContext();
        var due = await new TenantSubscriptionRepository(db).GetDueForRenewalAsync(nowUtc, AllRows);
        return due.Any(s => s.TenantId == _db.TenantId);
    }

    private async Task ApplyStatusAsync(bool isActive, long version)
    {
        await using var db = _db.NewContext();
        var subscription = await db.Subscriptions.IgnoreQueryFilters().SingleAsync(s => s.TenantId == _db.TenantId);
        Assert.True(subscription.ApplyTenantStatus(isActive, version, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }
}
