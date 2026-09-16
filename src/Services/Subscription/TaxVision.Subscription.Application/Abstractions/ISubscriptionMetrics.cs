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
}
