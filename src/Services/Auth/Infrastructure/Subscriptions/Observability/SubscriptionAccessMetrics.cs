using System.Diagnostics.Metrics;
using TaxVision.Auth.Application.Subscriptions.Abstractions;

namespace TaxVision.Auth.Infrastructure.Subscriptions.Observability;

/// <summary>Expiración/Dunning (Fase 6) — Meter singleton del corte/reactivación de acceso por facturación,
/// consumido por DI en <c>TenantSubscriptionAccessConsumer</c> y recolectado por el <c>MeterProvider</c> vía
/// <c>AddTaxVisionOpenTelemetry(..., SubscriptionAccessMetrics.MeterName)</c> en Program.cs.</summary>
public sealed class SubscriptionAccessMetrics : ISubscriptionAccessMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Auth.Subscriptions";

    private readonly Meter _meter;
    private readonly Counter<long> _accessBlockedTotal;
    private readonly Counter<long> _accessRestoredTotal;

    public SubscriptionAccessMetrics()
    {
        _meter = new Meter(MeterName);
        _accessBlockedTotal = _meter.CreateCounter<long>("subscription.access_blocked_total");
        _accessRestoredTotal = _meter.CreateCounter<long>("subscription.access_restored_total");
    }

    public void RecordAccessBlocked(string status) =>
        _accessBlockedTotal.Add(1, new KeyValuePair<string, object?>("status", status));

    public void RecordAccessRestored() => _accessRestoredTotal.Add(1);

    public void Dispose() => _meter.Dispose();
}
