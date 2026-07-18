namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// PlanChangeRequest ahora modela SOLO upgrades (ver PlanChangeRequest) — un downgrade nunca
/// pasa por acá, usa PendingDowngrade en su lugar.
/// </summary>
public enum PlanChangeRequestStatus
{
    /// <summary>Cargo del precio completo del nuevo plan pendiente de confirmación de
    /// PaymentApp. El plan todavía NO cambió — <see cref="TenantSubscription.ChangePlan"/> se
    /// llama recién cuando este request pasa a Applied.</summary>
    AwaitingPayment,

    Applied,

    /// <summary>El cargo del upgrade falló. El plan se queda como estaba — no hay nada que
    /// revertir porque nunca se aplicó.</summary>
    PaymentFailed,
}
