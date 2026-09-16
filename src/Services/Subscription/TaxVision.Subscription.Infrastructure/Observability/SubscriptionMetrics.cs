using System.Diagnostics.Metrics;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Infrastructure.Observability;

/// <summary>
/// Métricas custom de Subscription — un único <see cref="Meter"/> singleton, alimentado por los
/// handlers/jobs del ciclo de vida de add-ons y recolectado por el MeterProvider (se inyecta por
/// nombre en <c>AddTaxVisionOpenTelemetry</c>). Todos los contadores llevan el tag <c>add_on_code</c>.
/// </summary>
public sealed class SubscriptionMetrics : ISubscriptionMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Subscription";

    private readonly Meter _meter;
    private readonly Counter<long> _purchasedTotal;
    private readonly Counter<long> _absorbedTotal;
    private readonly Counter<long> _expiredTotal;
    private readonly Counter<long> _billedCentsTotal;
    private readonly Counter<long> _seatsPurchasedTotal;
    private readonly Counter<long> _seatsBilledCentsTotal;

    public SubscriptionMetrics()
        : this(MeterName) { }

    // El nombre es parametrizable solo para poder aislar el Meter en tests con MeterListener.
    public SubscriptionMetrics(string meterName)
    {
        _meter = new Meter(meterName);
        _purchasedTotal = _meter.CreateCounter<long>("subscription.addons.purchased_total");
        _absorbedTotal = _meter.CreateCounter<long>("subscription.addons.absorbed_total");
        _expiredTotal = _meter.CreateCounter<long>("subscription.addons.expired_total");
        _billedCentsTotal = _meter.CreateCounter<long>("subscription.addons.billed_cents_total");
        _seatsPurchasedTotal = _meter.CreateCounter<long>("subscription.seats.purchased_total");
        _seatsBilledCentsTotal = _meter.CreateCounter<long>("subscription.seats.billed_cents_total");
    }

    public void RecordAddOnPurchased(string addOnCode) =>
        _purchasedTotal.Add(1, new KeyValuePair<string, object?>("add_on_code", addOnCode));

    public void RecordAddOnAbsorbed(string addOnCode) =>
        _absorbedTotal.Add(1, new KeyValuePair<string, object?>("add_on_code", addOnCode));

    public void RecordAddOnExpired(string addOnCode) =>
        _expiredTotal.Add(1, new KeyValuePair<string, object?>("add_on_code", addOnCode));

    public void RecordAddOnBilled(string addOnCode, long amountCents) =>
        _billedCentsTotal.Add(amountCents, new KeyValuePair<string, object?>("add_on_code", addOnCode));

    public void RecordSeatsPurchased(string seatType, int quantity) =>
        _seatsPurchasedTotal.Add(quantity, new KeyValuePair<string, object?>("seat_type", seatType));

    public void RecordSeatsBilled(string seatType, long amountCents) =>
        _seatsBilledCentsTotal.Add(amountCents, new KeyValuePair<string, object?>("seat_type", seatType));

    public void Dispose() => _meter.Dispose();
}
