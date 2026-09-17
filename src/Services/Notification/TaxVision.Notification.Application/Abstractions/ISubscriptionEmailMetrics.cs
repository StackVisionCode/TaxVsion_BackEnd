namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Métrica de los emails de ciclo de vida de la suscripción (Expiración/Dunning, Fase 6). La
/// implementación concreta (Infrastructure) conoce el <c>Meter</c>. No se taggea tenantId (cardinalidad/PII).</summary>
public interface ISubscriptionEmailMetrics
{
    /// <summary>Un email de dunning/ciclo de vida se encoló. <paramref name="type"/> = templateKey
    /// (subscription.payment_failed | subscription.suspended | subscription.expired | subscription.reactivated).</summary>
    void RecordDunningEmailSent(string type);
}
