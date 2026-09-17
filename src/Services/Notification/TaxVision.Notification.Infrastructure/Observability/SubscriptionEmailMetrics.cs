using System.Diagnostics.Metrics;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Infrastructure.Observability;

/// <summary>Expiración/Dunning (Fase 6) — Meter singleton de los emails de ciclo de vida de la suscripción,
/// recolectado por el <c>MeterProvider</c> vía <c>AddTaxVisionOpenTelemetry(..., SubscriptionEmailMetrics.MeterName)</c>
/// en Program.cs.</summary>
public sealed class SubscriptionEmailMetrics : ISubscriptionEmailMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Notification.Subscriptions";

    private readonly Meter _meter;
    private readonly Counter<long> _dunningEmailSentTotal;

    public SubscriptionEmailMetrics()
    {
        _meter = new Meter(MeterName);
        _dunningEmailSentTotal = _meter.CreateCounter<long>("subscription.dunning_email_sent_total");
    }

    public void RecordDunningEmailSent(string type) =>
        _dunningEmailSentTotal.Add(1, new KeyValuePair<string, object?>("type", type));

    public void Dispose() => _meter.Dispose();
}
