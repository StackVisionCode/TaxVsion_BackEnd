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
/// <para>
/// Orden de las capas: edición contratada → notas de la oficina (plan a la medida) → add-ons → asientos.
/// La nota reemplaza el valor DEL PLAN y no el final: así lo que la oficina paga aparte (add-ons, asientos)
/// se sigue sumando encima y nunca queda inútil.
/// </para>
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
        ITenantEntitlementOverrideRepository overrides,
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
        var nowUtc = DateTime.UtcNow;
        // Igual que los add-ons: sin acceso (oficina que no paga) las notas no cuentan y la foto queda como hoy.
        var overridden = grantsAccess
            ? await ApplyOverridesAsync(tenantId, overrides, entries, nowUtc, ct)
            : (IReadOnlySet<string>)new HashSet<string>();
        if (grantsAccess)
            await MergeActiveAddOnsAsync(tenantId, tenantAddOns, addOnDefinitions, entries, ct);
        KeepOverrideMark(entries, overridden);

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
            nowUtc
        );
    }

    /// <summary>
    /// Pone encima del plan las notas vigentes de la oficina y devuelve qué claves tocó. Una nota vencida se
    /// ignora aquí mismo: aunque el job de vencimientos se atrase, ningún recálculo la vuelve a aplicar.
    /// </summary>
    private static async Task<IReadOnlySet<string>> ApplyOverridesAsync(
        Guid tenantId,
        ITenantEntitlementOverrideRepository overrides,
        Dictionary<string, EntitlementEntry> entries,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var applied = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in await overrides.GetByTenantIdAsync(tenantId, ct))
        {
            if (!item.IsInForceAt(nowUtc))
                continue;

            entries[item.Key] = item.ToEntry();
            applied.Add(item.Key);
        }

        return applied;
    }

    /// <summary>
    /// Un add-on que suma sobre una nota deja la línea marcada como AddOn (así marca el merger). Se vuelve a
    /// marcar como Override para que los consumidores sepan que la base de ese límite es una excepción de la
    /// oficina (Communication la deja pasar el máximo global por eso).
    /// </summary>
    private static void KeepOverrideMark(Dictionary<string, EntitlementEntry> entries, IReadOnlySet<string> overridden)
    {
        foreach (var key in overridden)
            if (entries.TryGetValue(key, out var entry) && entry.Source != EntitlementSource.Override)
                entries[key] = entry with { Source = EntitlementSource.Override };
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
