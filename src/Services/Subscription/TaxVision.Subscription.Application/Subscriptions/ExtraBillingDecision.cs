using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions;

/// <summary>Qué hacer con un extra (asiento o add-on) que vence, según cómo esté la suscripción base.</summary>
public enum ExtraBillingDecision
{
    /// <summary>La base está al día (o en dunning, que todavía da acceso): se cobra como siempre.</summary>
    Renew,

    /// <summary>La base no da acceso pero puede volver: no se cobra y el extra espera.</summary>
    Pause,

    /// <summary>La base terminó: el extra se cancela en vez de quedar cobrándose o colgado.</summary>
    Cancel,
}

/// <summary>
/// C5 — asientos y add-ons se seguían cobrando con la base cancelada, suspendida o vencida, sin dar nada a
/// cambio. La regla usa la misma lista de estados que los entitlements: se cobra mientras el tenant recibe.
/// </summary>
public static class ExtraBilling
{
    public static ExtraBillingDecision Decide(SubscriptionStatus baseStatus)
    {
        if (SubscriptionAccess.GrantsAccess(baseStatus))
            return ExtraBillingDecision.Renew;

        return SubscriptionAccess.IsTerminal(baseStatus) ? ExtraBillingDecision.Cancel : ExtraBillingDecision.Pause;
    }
}
