namespace TaxVision.Subscription.Application.Abstractions;

/// <summary>Métricas custom del ciclo de vida de add-ons que alimentan handlers/jobs. La
/// implementación concreta (Infrastructure) es la que conoce OpenTelemetry/<c>Meter</c>.</summary>
public interface ISubscriptionMetrics
{
    void RecordAddOnPurchased(string addOnCode);
    void RecordAddOnAbsorbed(string addOnCode);
    void RecordAddOnExpired(string addOnCode);

    /// <summary>Ingreso facturado (intent de cobro) de un add-on, en centavos.</summary>
    void RecordAddOnBilled(string addOnCode, long amountCents);

    /// <summary>Asientos de staff comprados, por tipo (suma de la cantidad).</summary>
    void RecordSeatsPurchased(string seatType, int quantity);

    /// <summary>Ingreso facturado (intent de cobro) de asientos, en centavos.</summary>
    void RecordSeatsBilled(string seatType, long amountCents);

    /// <summary>Transición de estado del ciclo de vida de la suscripción base (Expiración/Dunning). Tags
    /// <c>from</c>/<c>to</c>/<c>reason</c> — nunca lleva tenantId (cardinalidad/PII).</summary>
    void RecordStatusTransition(string from, string to, string reason);

    /// <summary>Resultado de una renovación/reactivación self-service. <paramref name="outcome"/> =
    /// <c>succeeded</c> | <c>failed</c>.</summary>
    void RecordSelfServiceRenewal(string outcome);
}
