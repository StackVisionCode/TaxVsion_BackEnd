using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Watch;

namespace TaxVision.Connectors.Infrastructure.Persistence.Repositories;

public sealed class ProviderWatchSubscriptionRepository(ConnectorsDbContext dbContext)
    : IProviderWatchSubscriptionRepository
{
    public async Task AddAsync(ProviderWatchSubscription subscription, CancellationToken ct = default) =>
        await dbContext.ProviderWatchSubscriptions.AddAsync(subscription, ct);

    public async Task<Result<ProviderWatchSubscription>> GetByAccountIdAsync(
        Guid accountId,
        CancellationToken ct = default
    )
    {
        var subscription = await dbContext.ProviderWatchSubscriptions.FirstOrDefaultAsync(
            s => s.AccountId == accountId,
            ct
        );
        return subscription is null
            ? Result.Failure<ProviderWatchSubscription>(
                new Error(
                    "ProviderWatchSubscription.NotFound",
                    $"ProviderWatchSubscription for account {accountId} not found."
                )
            )
            : Result.Success(subscription);
    }

    public async Task<Result<ProviderWatchSubscription>> GetByIdAsync(
        Guid subscriptionId,
        CancellationToken ct = default
    )
    {
        var subscription = await dbContext.ProviderWatchSubscriptions.FirstOrDefaultAsync(
            s => s.Id == subscriptionId,
            ct
        );
        return subscription is null
            ? Result.Failure<ProviderWatchSubscription>(
                new Error(
                    "ProviderWatchSubscription.NotFound",
                    $"ProviderWatchSubscription {subscriptionId} not found."
                )
            )
            : Result.Success(subscription);
    }

    public async Task<Result<ProviderWatchSubscription>> GetBySubscriptionRefAsync(
        string subscriptionRef,
        CancellationToken ct = default
    )
    {
        var subscription = await dbContext.ProviderWatchSubscriptions.FirstOrDefaultAsync(
            s => s.SubscriptionRef == subscriptionRef,
            ct
        );
        return subscription is null
            ? Result.Failure<ProviderWatchSubscription>(
                new Error(
                    "ProviderWatchSubscription.NotFound",
                    $"ProviderWatchSubscription with ref '{subscriptionRef}' not found."
                )
            )
            : Result.Success(subscription);
    }

    public async Task<IReadOnlyList<ProviderWatchSubscription>> ListExpiringBeforeAsync(
        DateTime thresholdUtc,
        CancellationToken ct = default
    )
    {
        return await dbContext
            .ProviderWatchSubscriptions.Where(s =>
                s.Status != ProviderWatchStatus.Failed
                && s.Status != ProviderWatchStatus.Removed
                && s.ExpiresAtUtc < thresholdUtc
            )
            .ToListAsync(ct);
    }
}
