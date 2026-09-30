namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// Claves de idempotencia deterministas por período: si el job de renovación corre dos
/// veces para el mismo período, el segundo intento no crea un renewal duplicado (ver
/// guardrail de idempotencia y §24.2 del diseño).
/// </summary>
public static class IdempotencyKeyFactory
{
    public static string SubscriptionRenewal(Guid tenantSubscriptionId, DateTime periodEndUtc) =>
        $"subscription-renewal-{tenantSubscriptionId:N}-{periodEndUtc:yyyyMMdd}";

    public static string SeatRenewal(Guid seatId, DateTime periodEndUtc) =>
        $"seat-renewal-{seatId:N}-{periodEndUtc:yyyyMMdd}";

    public static string AddOnRenewal(Guid tenantAddOnId, DateTime periodEndUtc) =>
        $"addon-renewal-{tenantAddOnId:N}-{periodEndUtc:yyyyMMdd}";

    /// <summary>Cargo prorrateado del período parcial inicial. Prefijo distinto de la renovación para
    /// no colisionar aunque coincida la fecha con el fin del período co-terminado.</summary>
    public static string AddOnInitialCharge(Guid tenantAddOnId, DateTime periodStartUtc) =>
        $"addon-initial-{tenantAddOnId:N}-{periodStartUtc:yyyyMMdd}";

    /// <summary>Cargo prorrateado del período parcial inicial de un seat (compra). Prefijo distinto de la
    /// renovación para no colisionar aunque coincida la fecha con el fin del período co-terminado.</summary>
    public static string SeatInitialCharge(Guid seatId, DateTime periodStartUtc) =>
        $"seat-initial-{seatId:N}-{periodStartUtc:yyyyMMdd}";

    /// <summary>Cargo por hosted-checkout de una compra de asientos. Único por intención (cada compra crea su
    /// propia <c>SeatPurchaseIntent</c>) — el re-submit de la misma intención replaya su sesión.</summary>
    public static string SeatCheckout(Guid seatPurchaseIntentId) => $"seat-checkout-{seatPurchaseIntentId:N}";

    /// <summary>Cargo por hosted-checkout de la compra de un add-on. Único por intención — el re-submit de la
    /// misma intención replaya su sesión.</summary>
    public static string AddOnCheckout(Guid addOnPurchaseIntentId) => $"addon-checkout-{addOnPurchaseIntentId:N}";

    /// <summary>Cargo por hosted-checkout de una renovación/reactivación self-service. Único por intención
    /// (cada intento crea su propia <c>SubscriptionRenewalIntent</c>) — el re-submit replaya su sesión.</summary>
    public static string RenewalCheckout(Guid renewalIntentId) => $"subscription-renewal-checkout-{renewalIntentId:N}";

    /// <summary>No es determinista por período como las renovaciones — un upgrade de plan es
    /// un cargo puntual, no periódico. <paramref name="chargeToken"/> es un Guid generado una
    /// sola vez por el caller al crear el PlanChangeRequest; esta key solo protege contra
    /// redelivery del mensaje en el bus, no contra un segundo submit del usuario (eso lo
    /// bloquea TenantSubscription.RequestPlanChange rechazando si ya hay un AwaitingPayment).</summary>
    public static string PlanChangeCharge(Guid chargeToken) => $"plan-change-{chargeToken:N}";
}
