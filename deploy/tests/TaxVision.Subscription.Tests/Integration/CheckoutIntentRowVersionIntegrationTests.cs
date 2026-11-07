using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Persistence;
using TaxVision.Subscription.Tests.TestDoubles;

namespace TaxVision.Subscription.Tests.Integration;

/// <summary>SUB-1: las intenciones de add-on y de renovación también tienen token de concurrencia.</summary>
public sealed class CheckoutIntentRowVersionIntegrationTests : IAsyncLifetime
{
    private readonly SubscriptionIntegrationDb _db = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => _db.CleanupAsync();

    [Fact]
    public async Task AddOnAndRenewalIntents_HaveConcurrencyToken()
    {
        var nowUtc = DateTime.UtcNow;
        var addOnIntent = AddOnPurchaseIntent
            .Create(
                _db.TenantId,
                AddOnTestCatalog.QuotaAddOn("test.pack"),
                quantity: 1,
                autoRenew: true,
                Money.Create(29m, "USD").Value,
                BillingCycle.Monthly,
                proratedTotalCents: 2900,
                Guid.NewGuid(),
                nowUtc
            )
            .Value;
        var renewalIntent = SubscriptionRenewalIntent
            .Create(_db.TenantId, 4900, "USD", BillingCycle.Monthly, Guid.NewGuid(), nowUtc)
            .Value;
        await using (var seed = _db.NewContext())
        {
            seed.AddOnPurchaseIntents.Add(addOnIntent);
            seed.SubscriptionRenewalIntents.Add(renewalIntent);
            await seed.SaveChangesAsync();
        }

        await AssertSecondWriterConflictsAsync(
            db => db.AddOnPurchaseIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == addOnIntent.Id),
            intent => intent.Cancel(Guid.NewGuid(), nowUtc),
            intent => intent.MarkPaid(nowUtc)
        );
        await AssertSecondWriterConflictsAsync(
            db => db.SubscriptionRenewalIntents.IgnoreQueryFilters().SingleAsync(i => i.Id == renewalIntent.Id),
            intent => intent.Cancel(Guid.NewGuid(), nowUtc),
            intent => intent.MarkPaid(nowUtc)
        );
    }

    private async Task AssertSecondWriterConflictsAsync<T>(
        Func<SubscriptionDbContext, Task<T>> load,
        Func<T, BuildingBlocks.Results.Result> firstChange,
        Func<T, BuildingBlocks.Results.Result> secondChange
    )
    {
        await using var first = _db.NewContext();
        await using var second = _db.NewContext();
        var a = await load(first);
        var b = await load(second);

        Assert.True(firstChange(a).IsSuccess);
        await first.SaveChangesAsync();

        Assert.True(secondChange(b).IsSuccess);
        await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
    }
}
