using BuildingBlocks.Caching;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Seats;
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
            command.TenantId,
            subscriptions,
            plans,
            seats,
            tenantAddOns,
            addOnDefinitions,
            snapshots,
            ct
        );
        if (rebuilt.IsFailure)
            return Result.Failure(rebuilt.Error);

        var snapshot = rebuilt.Value;
        var changedKeys = ComputeChangedKeys(previous, snapshot);
        var maxStaffUsers = await ComputeMaxStaffUsersAsync(command.TenantId, snapshot, seats, ct);

        await snapshots.UpsertAsync(snapshot, ct);
        await cache.RemoveAsync(EntitlementCacheKeys.Summary(command.TenantId), ct);

        await bus.PublishAsync(
            new TenantEntitlementsChangedIntegrationEvent
            {
                TenantId = command.TenantId,
                RevisionNumber = snapshot.RevisionNumber,
                ChangedKeys = changedKeys,
                PlanCode = snapshot.PlanCode,
                SubscriptionStatus = snapshot.SubscriptionStatus,
                SeatCount = snapshot.SeatCount,
                AvailableSeatCount = snapshot.AvailableSeatCount,
                MaxStaffUsers = maxStaffUsers,
                EntitlementValues = BuildEntitlementValues(snapshot),
                CorrelationId = correlation.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Entitlement snapshot recalculated for tenant {TenantId}: revision {Revision}, {ChangedCount} key(s) changed.",
            command.TenantId,
            snapshot.RevisionNumber,
            changedKeys.Length
        );
        return Result.Success();
    }

    private const string SeatsMaxKey = "seats.max";

    /// <summary>Cupo efectivo de usuarios STAFF = `seats.max` incluido en el plan + asientos STAFF
    /// (<see cref="SeatType.Standard"/>) no terminales comprados. Es lo que Auth hace cumplir; los tipos
    /// de asiento no-staff (Portal/Signature/ReadOnly/ServiceAccount) son pools aparte y no cuentan.</summary>
    private static async Task<int> ComputeMaxStaffUsersAsync(
        Guid tenantId,
        TenantEntitlementSnapshot snapshot,
        ISubscriptionSeatRepository seats,
        CancellationToken ct
    )
    {
        var tenantSeats = await seats.GetByTenantIdAsync(tenantId, ct);
        return ParseIncludedSeats(snapshot) + CountActiveStaffSeats(tenantSeats);
    }

    /// <summary>Asientos STAFF (<see cref="SeatType.Standard"/>) no terminales — los que suman al cupo de
    /// usuarios del tenant, por encima del <c>seats.max</c> incluido en el plan. Los otros tipos
    /// (Portal/Signature/ReadOnly/ServiceAccount) son pools aparte y no cuentan.</summary>
    public static int CountActiveStaffSeats(IReadOnlyList<SubscriptionSeat> seats) =>
        seats.Count(seat =>
            seat.Type == SeatType.Standard
            && seat.Status is not (SeatStatus.Cancelled or SeatStatus.Expired or SeatStatus.Released)
        );

    private static int ParseIncludedSeats(TenantEntitlementSnapshot snapshot)
    {
        foreach (var entry in snapshot.Entries)
        {
            if (entry.Key.Value == SeatsMaxKey && int.TryParse(entry.Value, out var max))
                return max;
        }

        return 0;
    }

    private static IReadOnlyDictionary<string, string> BuildEntitlementValues(TenantEntitlementSnapshot snapshot)
    {
        var values = new Dictionary<string, string>();
        foreach (var entry in snapshot.Entries)
            values[entry.Key.Value] = entry.Value;

        return values;
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
