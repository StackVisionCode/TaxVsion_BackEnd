namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <summary><paramref name="Kind"/> es "Upgrade" o "Downgrade". Para un upgrade,
/// <paramref name="ChargeAmountCents"/>/<paramref name="ChargeCurrency"/> traen el precio
/// completo cobrado (o por cobrar) y <paramref name="EffectiveAtUtc"/> es null. Para un
/// downgrade, <paramref name="EffectiveAtUtc"/> trae la fecha de la próxima renovación y los
/// campos de cargo son null — un downgrade nunca cobra nada.</summary>
public sealed record PendingPlanChangeResponse(
    string Kind,
    Guid Id,
    string FromPlanCode,
    string ToPlanCode,
    string? ToBillingCycle,
    string Status,
    DateTime RequestedAtUtc,
    DateTime? EffectiveAtUtc,
    long? ChargeAmountCents,
    string? ChargeCurrency
);
