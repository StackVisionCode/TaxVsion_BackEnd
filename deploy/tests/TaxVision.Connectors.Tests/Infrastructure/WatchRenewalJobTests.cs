using BuildingBlocks.Common;
using BuildingBlocks.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;
using TaxVision.Connectors.Infrastructure.Jobs;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Sync;
using TaxVision.Connectors.Tests.Watch;

namespace TaxVision.Connectors.Tests.Infrastructure;

public class WatchRenewalJobTests
{
    private const string LeaderLockKey = "connectors:watch-renewal:leader";

    private sealed class CountingRenewalService : IWatchRenewalService
    {
        public List<Guid> Renewed { get; } = [];

        public Task<Result> RenewAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            Renewed.Add(subscriptionId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> RecreateAsync(Guid subscriptionId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private static (WatchRenewalJob Job, CountingRenewalService Renewals) CreateJob(
        IDistributedLock distributedLock,
        int subscriptions
    )
    {
        var repository = new FakeProviderWatchSubscriptionRepository();
        var now = DateTime.UtcNow;
        for (var index = 0; index < subscriptions; index++)
            repository.Subscriptions.Add(
                ProviderWatchSubscription
                    .Create(Guid.NewGuid(), ProviderCode.Gmail, $"history-{index}", "topic", now.AddHours(1), now)
                    .Value
            );

        var renewals = new CountingRenewalService();
        var services = new ServiceCollection();
        services.AddSingleton(distributedLock);
        services.AddSingleton<IProviderWatchSubscriptionRepository>(repository);
        services.AddSingleton<IWatchRenewalService>(renewals);
        services.AddScoped<ICorrelationContext, FakeCorrelationContext>();
        return (
            new WatchRenewalJob(
                services.BuildServiceProvider(),
                Options.Create(new WatchRenewalOptions()),
                NullLogger<WatchRenewalJob>.Instance
            ),
            renewals
        );
    }

    [Fact]
    public async Task AnotherInstanceHoldsTheLeaderLock_RenewsNothing()
    {
        var distributedLock = new InMemoryDistributedLock();
        await using var held = await distributedLock.AcquireAsync(LeaderLockKey, TimeSpan.FromMinutes(5));
        var (job, renewals) = CreateJob(distributedLock, subscriptions: 2);

        await job.RunOnceAsync(default);

        Assert.Empty(renewals.Renewed);
    }

    [Fact]
    public async Task LeaderLockLostMidSweep_StopsBeforeTheNextSubscription()
    {
        var (job, renewals) = CreateJob(new LostOnExtendDistributedLock(), subscriptions: 2);

        await job.RunOnceAsync(default);

        Assert.Empty(renewals.Renewed);
    }

    [Fact]
    public async Task LeaderLockFree_RenewsAllAndReleasesIt()
    {
        var distributedLock = new InMemoryDistributedLock();
        var (job, renewals) = CreateJob(distributedLock, subscriptions: 2);

        await job.RunOnceAsync(default);

        Assert.Equal(2, renewals.Renewed.Count);
        await using var free = await distributedLock.AcquireAsync(LeaderLockKey, TimeSpan.FromMinutes(5));
        Assert.True(free.IsAcquired);
    }
}
