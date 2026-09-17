namespace TaxVision.Auth.Application.Subscriptions.Abstractions;

/// <summary>Métricas del corte/reactivación de acceso por facturación (Expiración/Dunning, Fase 6). La
/// implementación concreta (Infrastructure) conoce el <c>Meter</c>. Nunca se taggea tenantId (cardinalidad/PII).</summary>
public interface ISubscriptionAccessMetrics
{
    /// <summary>Acceso cortado por lapso de suscripción. <paramref name="status"/> = Suspended | Expired.</summary>
    void RecordAccessBlocked(string status);

    /// <summary>Acceso restaurado (suscripción volvió a Active).</summary>
    void RecordAccessRestored();
}
