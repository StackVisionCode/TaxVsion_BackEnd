using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>CON-04: dos fallos de renovación leídos a la vez no se pisan el contador.</summary>
[Collection(ConnectorsJobHost.Collection)]
public sealed class ProviderWatchSubscriptionConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly ConnectorsJobHost _host = new("conwconc");

    public Task InitializeAsync() => _host.StartAsync(_ => { });

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact(Timeout = 120_000)]
    public async Task TwoRenewalFailures_FromStaleReads_SecondThrows()
    {
        var subscriptionId = await SeedAsync();

        await using var first = _host.NewContext();
        await using var second = _host.NewContext();
        var winner = await first.ProviderWatchSubscriptions.SingleAsync(s => s.Id == subscriptionId);
        var stale = await second.ProviderWatchSubscriptions.SingleAsync(s => s.Id == subscriptionId);

        winner.RecordRenewalFailure();
        await first.SaveChangesAsync();

        stale.RecordRenewalFailure();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => second.SaveChangesAsync());
        Assert.Equal("Persistence.ConcurrencyConflict", conflict.Code);

        // El reintento relee y suma sobre lo nuevo.
        await using var retry = _host.NewContext();
        var fresh = await retry.ProviderWatchSubscriptions.SingleAsync(s => s.Id == subscriptionId);
        fresh.RecordRenewalFailure();
        await retry.SaveChangesAsync();

        await using var read = _host.NewContext();
        Assert.Equal(3, (await read.ProviderWatchSubscriptions.SingleAsync(s => s.Id == subscriptionId)).FailureCount);
    }

    private async Task<Guid> SeedAsync()
    {
        var now = DateTime.UtcNow;
        var subscription = ProviderWatchSubscription
            .Create(Guid.NewGuid(), ProviderCode.Gmail, "history-1", "topic", now.AddHours(12), now)
            .Value;
        subscription.RecordRenewalFailure();
        await using var db = _host.NewContext();
        db.ProviderWatchSubscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return subscription.Id;
    }
}
