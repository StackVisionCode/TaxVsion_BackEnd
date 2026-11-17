using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.AddOns;

public enum AddOnTierChangeDirection
{
    Upgrade = 1,
    Downgrade = 2,
}

/// <summary>Qué pasa al cambiar de tamaño: hacia dónde, el precio nuevo, lo que se cobra hoy y desde cuándo aplica.</summary>
public sealed record AddOnTierChangePlan(
    AddOnTierChangeDirection Direction,
    Money TargetUnitPrice,
    Money ChargeNow,
    DateTime EffectiveAtUtc
);

/// <summary>
/// Cambio de tamaño dentro de un grupo exclusivo (p. ej. storage): subir aplica ya y cobra la diferencia
/// prorrateada hasta el fin del período; bajar aplica en la renovación, sin cobro, y solo si los archivos caben.
/// </summary>
public static class AddOnTierChangePolicy
{
    /// <summary>Motivo guardado cuando la bajada programada no se aplica porque los archivos no caben.</summary>
    public const string StorageWouldOverflowReason = "storage_would_overflow";

    private const string StorageEntitlementKey = "storage.max_bytes";

    public static Result<AddOnTierChangePlan> Plan(
        TenantAddOn current,
        AddOnDefinition currentDefinition,
        AddOnDefinition target,
        int activeInstancesInGroup,
        DateTime nowUtc
    )
    {
        if (current.Status != AddOnStatus.Active)
            return Fail("AddOn.TierChangeNotActive", "Only an active add-on can change its size.");

        if (current.HasRenewalInProgress)
            return Fail("AddOn.RenewalInProgress", "The add-on is finishing a renewal.");

        if (
            string.IsNullOrWhiteSpace(currentDefinition.MutexGroup)
            || !string.Equals(currentDefinition.MutexGroup, target.MutexGroup, StringComparison.OrdinalIgnoreCase)
        )
            return Fail("AddOn.TierChangeOutsideGroup", "The size can only change within the same add-on group.");

        if (target.Id == current.AddOnDefinitionId)
            return Fail("AddOn.TierChangeSameTier", "The add-on already has this size.");

        if (target.Status != AddOnDefinitionStatus.Published)
            return Fail("AddOn.TierChangeNotOffered", "This size is not offered.");

        // Con más de un paquete del grupo, cambiar uno dejaría dos tamaños distintos activos.
        if (current.Quantity != 1 || activeInstancesInGroup != 1)
            return Fail("AddOn.TierChangeRequiresSingleInstance", "Change the size when you have a single pack.");

        var targetPrice = target.ResolveUnitPrice(current.BillingCycle, current.Quantity);
        if (targetPrice.IsFailure)
            return Result.Failure<AddOnTierChangePlan>(targetPrice.Error);

        if (targetPrice.Value.Currency != current.UnitPrice.Currency)
            return Fail("AddOn.TierChangeCurrencyMismatch", "The new size is priced in another currency.");

        if (targetPrice.Value.Amount <= current.UnitPrice.Amount)
            return Result.Success(
                new AddOnTierChangePlan(
                    AddOnTierChangeDirection.Downgrade,
                    targetPrice.Value,
                    Money.Zero(targetPrice.Value.Currency),
                    current.CurrentPeriodEndUtc
                )
            );

        var difference = Money.Create(targetPrice.Value.Amount - current.UnitPrice.Amount, targetPrice.Value.Currency);
        if (difference.IsFailure)
            return Result.Failure<AddOnTierChangePlan>(difference.Error);

        var chargeNow = ProrationCalculator.InitialPeriod(
            difference.Value,
            current.CurrentPeriodStartUtc,
            current.CurrentPeriodEndUtc,
            nowUtc
        );
        if (chargeNow.IsFailure)
            return Result.Failure<AddOnTierChangePlan>(chargeNow.Error);

        return Result.Success(
            new AddOnTierChangePlan(AddOnTierChangeDirection.Upgrade, targetPrice.Value, chargeNow.Value, nowUtc)
        );
    }

    /// <summary>Los archivos caben si el uso no supera el total que queda al cambiar un paquete por otro.</summary>
    public static bool Fits(long usedBytes, long currentMaxBytes, AddOnDefinition current, AddOnDefinition target) =>
        usedBytes <= currentMaxBytes - StorageBytes(current) + StorageBytes(target);

    /// <summary>Cuánto almacenamiento suma el paquete (0 si no suma almacenamiento).</summary>
    public static long StorageBytes(AddOnDefinition definition)
    {
        var entitlement = definition.Entitlements.FirstOrDefault(e => e.Key.Value == StorageEntitlementKey);
        return entitlement is not null && long.TryParse(entitlement.Value, out var bytes) ? bytes : 0;
    }

    private static Result<AddOnTierChangePlan> Fail(string code, string message) =>
        Result.Failure<AddOnTierChangePlan>(new Error(code, message));
}
