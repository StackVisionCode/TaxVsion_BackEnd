using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Application.AddOns.TierChange;

public enum ScheduledTierChangeOutcome
{
    /// <summary>No había bajada programada.</summary>
    None = 0,

    /// <summary>Los archivos caben: pasa al tamaño y precio programados.</summary>
    Applied = 1,

    /// <summary>Ya no caben: conserva el tamaño actual y guarda el motivo.</summary>
    Skipped = 2,

    /// <summary>No se pudo medir: no se renueva todavía; el job lo vuelve a intentar.</summary>
    Postponed = 3,
}

/// <summary>Al renovar, decide la bajada programada con el uso medido fuera de la transacción. Nunca deja a la
/// oficina por encima de su cuota ni pierde archivos.</summary>
public static class ScheduledTierChangeAtRenewal
{
    public static ScheduledTierChangeOutcome Resolve(
        TenantAddOn addOn,
        AddOnDefinition current,
        AddOnDefinition pending,
        long? usedBytes,
        long? currentMaxBytes,
        DateTime nowUtc
    )
    {
        if (!addOn.HasScheduledTierChange)
            return ScheduledTierChangeOutcome.None;

        if (usedBytes is null || currentMaxBytes is null)
            return ScheduledTierChangeOutcome.Postponed;

        if (AddOnTierChangePolicy.Fits(usedBytes.Value, currentMaxBytes.Value, current, pending))
            return addOn.ApplyScheduledTierChange(Guid.Empty, nowUtc).IsSuccess
                ? ScheduledTierChangeOutcome.Applied
                : ScheduledTierChangeOutcome.Postponed;

        return addOn
            .SkipScheduledTierChange(AddOnTierChangePolicy.StorageWouldOverflowReason, Guid.Empty, nowUtc)
            .IsSuccess
            ? ScheduledTierChangeOutcome.Skipped
            : ScheduledTierChangeOutcome.Postponed;
    }
}
