namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <summary><paramref name="Kind"/> es "Upgrade" o "Downgrade". Para un upgrade,
/// <paramref name="ChargeAmountCents"/>/<paramref name="ChargeCurrency"/> traen el precio
/// completo cobrado (o por cobrar) y <paramref name="EffectiveAtUtc"/> es null. Para un
/// downgrade, <paramref name="EffectiveAtUtc"/> trae la fecha de la próxima renovación y los
/// campos de cargo son null — un downgrade nunca cobra nada.
/// <para><paramref name="CheckoutUrl"/> solo viene cuando el upgrade se está cobrando por redirect y la
/// sesión sigue viva: es el pago que el usuario dejó a medias y puede retomar.</para></summary>
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
    string? ChargeCurrency,
    string? CheckoutUrl = null
);
