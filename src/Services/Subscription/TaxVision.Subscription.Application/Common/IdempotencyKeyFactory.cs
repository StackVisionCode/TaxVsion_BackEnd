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

    /// <summary>No es determinista por período como las renovaciones — un upgrade de plan es
    /// un cargo puntual, no periódico. <paramref name="chargeToken"/> es un Guid generado una
    /// sola vez por el caller al crear el PlanChangeRequest; esta key solo protege contra
    /// redelivery del mensaje en el bus, no contra un segundo submit del usuario (eso lo
    /// bloquea TenantSubscription.RequestPlanChange rechazando si ya hay un AwaitingPayment).</summary>
    public static string PlanChangeCharge(Guid chargeToken) =>
        $"plan-change-{chargeToken:N}";
}
