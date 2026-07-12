using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Entitlements;
using Wolverine;

namespace TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;

public static class RecalculateEntitlementsHandler
{
    public static async Task<Result> Handle(
        RecalculateEntitlementsCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionSeatRepository seats,
        ITenantAddOnRepository tenantAddOns,
        IAddOnDefinitionRepository addOnDefinitions,
        ITenantEntitlementSnapshotRepository snapshots,
        ICacheService cache,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantEntitlementSnapshot> logger,
        CancellationToken ct
    )
    {
        var previous = await snapshots.GetByTenantIdAsync(command.TenantId, ct);

        var rebuilt = await EntitlementSnapshotBuilder.BuildAsync(
            command.TenantId, subscriptions, plans, seats, tenantAddOns, addOnDefinitions, snapshots, ct);
        if (rebuilt.IsFailure)
            return Result.Failure(rebuilt.Error);

        var snapshot = rebuilt.Value;
        var changedKeys = ComputeChangedKeys(previous, snapshot);

        await snapshots.UpsertAsync(snapshot, ct);
        await cache.RemoveAsync(EntitlementCacheKeys.Summary(command.TenantId), ct);

        await bus.PublishAsync(new TenantEntitlementsChangedIntegrationEvent
        {
            TenantId = command.TenantId,
            RevisionNumber = snapshot.RevisionNumber,
            ChangedKeys = changedKeys,
            PlanCode = snapshot.PlanCode,
            SubscriptionStatus = snapshot.SubscriptionStatus,
            CorrelationId = correlation.CorrelationId,
        });
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Entitlement snapshot recalculated for tenant {TenantId}: revision {Revision}, {ChangedCount} key(s) changed.",
            command.TenantId, snapshot.RevisionNumber, changedKeys.Length
        );
        return Result.Success();
    }

    private static string[] ComputeChangedKeys(TenantEntitlementSnapshot? previous, TenantEntitlementSnapshot current)
    {
        var previousValues = new Dictionary<string, string>();
        if (previous is not null)
        {
            foreach (var entry in previous.Entries)
                previousValues[entry.Key.Value] = entry.Value;
        }

        var changed = new List<string>();
        foreach (var entry in current.Entries)
        {
            if (!previousValues.TryGetValue(entry.Key.Value, out var previousValue) || previousValue != entry.Value)
                changed.Add(entry.Key.Value);
        }

        return changed.ToArray();
    }
}
