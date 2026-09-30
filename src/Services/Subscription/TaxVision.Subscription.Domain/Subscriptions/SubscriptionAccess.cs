namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Qué habilita la suscripción base según su estado. Es la MISMA lista para dar entitlements y para cobrar
/// los extras (asientos y add-ons): no se le cobra al tenant por algo que no está recibiendo.
/// </summary>
public static class SubscriptionAccess
{
    private static readonly SubscriptionStatus[] Granting =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
        SubscriptionStatus.PastDue,
        SubscriptionStatus.GracePeriod,
    ];

    /// <summary>Sin vuelta atrás por sí sola: los extras que cuelgan de ella tampoco tienen futuro.</summary>
    private static readonly SubscriptionStatus[] Terminal = [SubscriptionStatus.Cancelled, SubscriptionStatus.Expired];

    public static bool GrantsAccess(SubscriptionStatus status) => Array.IndexOf(Granting, status) >= 0;

    public static bool IsTerminal(SubscriptionStatus status) => Array.IndexOf(Terminal, status) >= 0;
}
