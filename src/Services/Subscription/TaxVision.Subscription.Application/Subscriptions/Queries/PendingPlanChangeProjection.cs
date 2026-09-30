using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <summary>
/// Qué cambio de plan mostrar, compartido por <c>GET /subscriptions/plan-change</c> y el read model del
/// Account, para que las dos pantallas digan lo mismo.
/// </summary>
internal static class PendingPlanChangeProjection
{
    /// <summary>
    /// Prioridad: un upgrade en vuelo (AwaitingPayment) es lo más urgente, luego un downgrade agendado. Si no
    /// hay nada accionable, el último upgrade fallido, para avisar que no se aplicó y hace falta otro método
    /// de pago.
    /// </summary>
    public static PendingPlanChangeResponse? From(TenantSubscription subscription)
    {
        var awaitingPayment = subscription.PlanChangeRequests.FirstOrDefault(request =>
            request.Status == PlanChangeRequestStatus.AwaitingPayment
        );
        if (awaitingPayment is not null)
            return FromUpgrade(awaitingPayment, DateTime.UtcNow);

        var scheduledDowngrade = subscription.PendingDowngrades.FirstOrDefault(pending =>
            pending.Status == PendingDowngradeStatus.Scheduled
        );
        if (scheduledDowngrade is not null)
            return FromDowngrade(scheduledDowngrade);

        var lastFailedUpgrade = subscription
            .PlanChangeRequests.Where(request => request.Status == PlanChangeRequestStatus.PaymentFailed)
            .OrderByDescending(request => request.RequestedAtUtc)
            .FirstOrDefault();

        return lastFailedUpgrade is null ? null : FromUpgrade(lastFailedUpgrade, DateTime.UtcNow);
    }

    private static PendingPlanChangeResponse FromUpgrade(PlanChangeRequest request, DateTime nowUtc) =>
        new(
            Kind: "Upgrade",
            request.Id,
            request.FromPlanCode,
            request.ToPlanCode,
            request.ToBillingCycle?.ToString(),
            request.Status.ToString(),
            request.RequestedAtUtc,
            EffectiveAtUtc: null,
            request.ChargeAmountCents,
            request.ChargeCurrency,
            // Solo si todavía se puede pagar: una sesión caducada no es un enlace que ofrecer.
            request.HasOpenCheckout(nowUtc)
                ? request.CheckoutUrl
                : null
        );

    private static PendingPlanChangeResponse FromDowngrade(PendingDowngrade pending) =>
        new(
            Kind: "Downgrade",
            pending.Id,
            pending.FromPlanCode,
            pending.ToPlanCode,
            pending.ToBillingCycle?.ToString(),
            pending.Status.ToString(),
            pending.RequestedAtUtc,
            pending.EffectiveAtUtc,
            ChargeAmountCents: null,
            ChargeCurrency: null
        );
}
