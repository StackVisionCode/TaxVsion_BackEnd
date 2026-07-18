using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;

namespace TaxVision.Connectors.Tests.Watch;

internal sealed class FakeProviderWatchSubscriptionRepository : IProviderWatchSubscriptionRepository
{
    public List<ProviderWatchSubscription> Subscriptions { get; } = [];

    public Task AddAsync(ProviderWatchSubscription subscription, CancellationToken ct = default)
    {
        Subscriptions.Add(subscription);
        return Task.CompletedTask;
    }

    public Task<Result<ProviderWatchSubscription>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var subscription = Subscriptions.Find(s => s.AccountId == accountId);
        return Task.FromResult(
            subscription is null
                ? Result.Failure<ProviderWatchSubscription>(
                    new Error("ProviderWatchSubscription.NotFound", "Not found.")
                )
                : Result.Success(subscription)
        );
    }

    public Task<Result<ProviderWatchSubscription>> GetByIdAsync(Guid subscriptionId, CancellationToken ct = default)
    {
        var subscription = Subscriptions.Find(s => s.Id == subscriptionId);
        return Task.FromResult(
            subscription is null
                ? Result.Failure<ProviderWatchSubscription>(
                    new Error("ProviderWatchSubscription.NotFound", "Not found.")
                )
                : Result.Success(subscription)
        );
    }

    public Task<Result<ProviderWatchSubscription>> GetBySubscriptionRefAsync(
        string subscriptionRef,
        CancellationToken ct = default
    )
    {
        var subscription = Subscriptions.Find(s => s.SubscriptionRef == subscriptionRef);
        return Task.FromResult(
            subscription is null
                ? Result.Failure<ProviderWatchSubscription>(
                    new Error("ProviderWatchSubscription.NotFound", "Not found.")
                )
                : Result.Success(subscription)
        );
    }

    public Task<IReadOnlyList<ProviderWatchSubscription>> ListExpiringBeforeAsync(
        DateTime thresholdUtc,
        CancellationToken ct = default
    )
    {
        IReadOnlyList<ProviderWatchSubscription> result = Subscriptions
            .Where(s =>
                s.Status != ProviderWatchStatus.Failed
                && s.Status != ProviderWatchStatus.Removed
                && s.ExpiresAtUtc < thresholdUtc
            )
            .ToList();
        return Task.FromResult(result);
    }
}

internal sealed class FakeWatchProviderClient(ProviderCode providerCode) : IWatchProviderClient
{
    public ProviderCode ProviderCode { get; } = providerCode;
    public Func<Guid, WatchSetupResult>? OnSetup { get; set; }
    public Func<Guid, string, WatchSetupResult>? OnRenew { get; set; }
    public Exception? ThrowOnSetup { get; set; }
    public Exception? ThrowOnRenew { get; set; }

    public Task<WatchSetupResult> SetupWatchAsync(Guid accountId, CancellationToken ct = default)
    {
        if (ThrowOnSetup is not null)
            throw ThrowOnSetup;

        return Task.FromResult(
            OnSetup?.Invoke(accountId) ?? new WatchSetupResult("subscription-ref", "topic", DateTime.UtcNow.AddDays(7))
        );
    }

    public Task<WatchSetupResult> RenewWatchAsync(
        Guid accountId,
        string subscriptionRef,
        CancellationToken ct = default
    )
    {
        if (ThrowOnRenew is not null)
            throw ThrowOnRenew;

        return Task.FromResult(
            OnRenew?.Invoke(accountId, subscriptionRef)
                ?? new WatchSetupResult(subscriptionRef, "topic", DateTime.UtcNow.AddDays(7))
        );
    }
}

internal sealed class FakeWatchProviderClientFactory(IWatchProviderClient? client) : IWatchProviderClientFactory
{
    public Result<IWatchProviderClient> Resolve(ProviderCode providerCode) =>
        client is null
            ? Result.Failure<IWatchProviderClient>(
                new Error("WatchProviderClientFactory.NotSupported", "Not supported.")
            )
            : Result.Success(client);
}
