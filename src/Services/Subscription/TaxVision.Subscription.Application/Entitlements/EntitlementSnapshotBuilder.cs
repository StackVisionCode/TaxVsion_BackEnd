using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Entitlements;

/// <summary>
/// Reconstruye el <see cref="TenantEntitlementSnapshot"/> de un tenant a partir de su
/// estado actual: edición CONTRATADA (con las reglas de <see cref="ContractedEntitlementPolicy"/>) + add-ons
/// activos + seats. Publicar una edición nueva no cambia la foto de nadie (D4). Determinista — el mismo
/// estado siempre produce el mismo conjunto de entries (salvo ComputedAtUtc/RevisionNumber).
/// No hay overrides administrativos todavía (fuera de alcance de esta fase).
/// </summary>
public static class EntitlementSnapshotBuilder
{
    // Estados que dan acceso: se mantiene el acceso durante PastDue (reintento de cobro) para no cortar por
    // un blip de pago. Fuera (Draft/Suspended/Cancelled/Expired) los módulos se apagan (fail-closed).

    public static async Task<Result<TenantEntitlementSnapshot>> BuildAsync(
        Guid tenantId,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IModuleRepository modules,
        ISubscriptionSeatRepository seats,
        ITenantAddOnRepository tenantAddOns,
        IAddOnDefinitionRepository addOnDefinitions,
        ITenantEntitlementSnapshotRepository snapshots,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(tenantId, ct);
        if (subscription is null)
            return Result.Failure<TenantEntitlementSnapshot>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var planVersion = PlanPricing.FindVersion(plan, subscription.PlanVersionId);
        if (plan is null || planVersion is null)
            return Result.Failure<TenantEntitlementSnapshot>(
                new Error("Plan.ContractedVersionNotFound", "The contracted plan edition does not exist.")
            );

        // Enforcement status-aware: si la base no da acceso, los módulos del plan se apagan y
        // los add-ons (dependientes) no cuentan. Los límites/cuotas se conservan (el gate es por módulo).
        var grantsAccess = SubscriptionAccess.GrantsAccess(subscription.Status);

        var baseEntries = ContractedEntitlementPolicy.BaseEntries(
            planVersion,
            await modules.GetAllAsync(ct),
            grantsAccess
        );
        if (baseEntries.IsFailure)
            return Result.Failure<TenantEntitlementSnapshot>(baseEntries.Error);

        var entries = baseEntries.Value;
        if (grantsAccess)
            await MergeActiveAddOnsAsync(tenantId, tenantAddOns, addOnDefinitions, entries, ct);

        var (seatCount, availableSeatCount) = await CountSeatsAsync(tenantId, seats, ct);
        var previousRevision = (await snapshots.GetByTenantIdAsync(tenantId, ct))?.RevisionNumber ?? 0;

        return TenantEntitlementSnapshot.Rebuild(
            tenantId,
            previousRevision,
            plan.Code.Value,
            planVersion.Id,
            subscription.Status.ToString(),
            seatCount,
            availableSeatCount,
            entries.Values.ToList(),
            DateTime.UtcNow
        );
    }

    private static async Task MergeActiveAddOnsAsync(
        Guid tenantId,
        ITenantAddOnRepository tenantAddOns,
        IAddOnDefinitionRepository addOnDefinitions,
        Dictionary<string, EntitlementEntry> entries,
        CancellationToken ct
    )
    {
        var addOns = await tenantAddOns.GetByTenantIdAsync(tenantId, ct);

        foreach (var addOn in addOns)
        {
            if (addOn.Status != AddOnStatus.Active)
                continue;

            var definition = await addOnDefinitions.GetByIdAsync(addOn.AddOnDefinitionId, ct);
            if (definition is null)
                continue;

            foreach (var feature in definition.Features)
            {
                if (!feature.Enabled)
                    continue;

                entries.TryGetValue(feature.FeatureKey.Value, out var existing);
                entries[feature.FeatureKey.Value] = EntitlementMerger.MergeAddOnValue(
                    existing,
                    feature.FeatureKey,
                    EntitlementValueType.Bool,
                    "true",
                    AddOnMergeStrategy.Or
                );
            }

            foreach (var entitlement in definition.Entitlements)
            {
                entries.TryGetValue(entitlement.Key.Value, out var existing);
                entries[entitlement.Key.Value] = EntitlementMerger.MergeAddOnValue(
                    existing,
                    entitlement.Key,
                    entitlement.ValueType,
                    entitlement.Value,
                    entitlement.MergeStrategy
                );
            }
        }
    }

    private static async Task<(int SeatCount, int AvailableSeatCount)> CountSeatsAsync(
        Guid tenantId,
        ISubscriptionSeatRepository seats,
        CancellationToken ct
    )
    {
        var tenantSeats = await seats.GetByTenantIdAsync(tenantId, ct);

        var seatCount = 0;
        var availableSeatCount = 0;
        foreach (var seat in tenantSeats)
        {
            // Terminales por dominio: Cancelled/Expired ya nunca vuelven y no cuentan al cupo.
            // Suspended también se excluye: significa que agotamos los reintentos de cobro — hasta que
            // el admin lo renueve (ReactivateAfterAdminReview → Active) deja de ser cupo vendido. Si se
            // mantuviera dentro del cupo, el tenant seguiría pudiendo invitar empleados a seats que ya
            // nadie está pagando. PastDue sí sigue contando: es la ventana de reintentos (grace natural).
            if (seat.Status is SeatStatus.Cancelled or SeatStatus.Expired or SeatStatus.Suspended)
                continue;

            seatCount++;
            if (seat.Status == SeatStatus.Available)
                availableSeatCount++;
        }

        return (seatCount, availableSeatCount);
    }
}
