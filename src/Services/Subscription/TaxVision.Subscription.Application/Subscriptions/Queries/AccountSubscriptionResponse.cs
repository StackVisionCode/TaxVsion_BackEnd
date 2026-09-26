namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <summary>
/// Todo lo que el Account necesita de Subscription en una sola llamada. Los asientos USADOS no están acá:
/// solo Auth sabe cuántos usuarios activos hay, así que el front compone con <c>GET /auth/tenants/limits</c>.
/// </summary>
public sealed record AccountSubscriptionResponse(
    AccountPlanView Plan,
    AccountPeriodView Period,
    PendingPlanChangeResponse? PendingPlanChange,
    AccountSeatsView Seats,
    IReadOnlyList<AccountAddOnView> AddOns,
    AccountOpenSeatCheckoutView? OpenSeatCheckout
);

/// <summary>
/// El plan tal como se CONTRATÓ (la versión que firmó el tenant), no la publicada hoy: si el catálogo
/// cambió de precio o de límites, el tenant sigue con lo suyo hasta que renueve o cambie de plan.
/// </summary>
public sealed record AccountPlanView(
    string Code,
    string Name,
    string Status,
    string BillingCycle,
    long CurrentCyclePriceCents,
    string Currency,
    IReadOnlyList<string> EnabledModules,
    bool BillingAccessBlocked
);

/// <summary>Período vigente y las fechas que la pantalla usa para decir cuándo se cobra o cuándo se corta.</summary>
public sealed record AccountPeriodView(
    DateTime CurrentPeriodStartUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime? NextRenewalAtUtc,
    DateTime? TrialEndsAtUtc,
    DateTime? GracePeriodEndsAtUtc,
    /// <summary>Cancelación programada: el acceso llega hasta <c>CurrentPeriodEndUtc</c> y se puede deshacer.</summary>
    bool CancelAtPeriodEnd,
    DateTime? CancelledAtUtc
);

/// <summary>
/// Compra de asientos que quedó a medias y todavía se puede pagar. Mientras exista, pedir otra distinta se
/// rechaza (evita dos cobros en vuelo), así que la pantalla ofrece retomar esta.
/// </summary>
public sealed record AccountOpenSeatCheckoutView(
    Guid IntentId,
    string SeatType,
    int Quantity,
    long TotalCents,
    string Currency,
    string CheckoutUrl,
    DateTime ExpiresAtUtc
);

/// <summary>Asientos que paga el tenant: los que trae el plan más los comprados aparte.</summary>
public sealed record AccountSeatsView(int IncludedInPlan, int Purchased, int Total);

/// <summary>Estado de un add-on del catálogo para ESTE tenant.</summary>
public static class AddOnEligibility
{
    /// <summary>El plan contratado ya trae sus módulos: no hay nada que comprar.</summary>
    public const string Included = "Included";

    /// <summary>Comprado y vigente.</summary>
    public const string Active = "Active";

    /// <summary>Se puede comprar.</summary>
    public const string Available = "Available";
}

/// <summary>
/// Un add-on del catálogo visto desde este tenant. <paramref name="UnitAmountCents"/> es null cuando el
/// add-on no tiene precio para el ciclo de facturación del tenant; los campos del final solo vienen con
/// <see cref="AddOnEligibility.Active"/>.
/// </summary>
public sealed record AccountAddOnView(
    string Code,
    string Name,
    string Description,
    string Category,
    string Eligibility,
    long? UnitAmountCents,
    string? Currency,
    Guid? TenantAddOnId,
    DateTime? CurrentPeriodEndUtc,
    bool? AutoRenew
);
