using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Domain.SaaSPayments;

namespace TaxVision.PaymentApp.Infrastructure.Observability;

/// <summary>
/// Catálogo de métricas custom de PaymentApp (§29.2 del diseño) — un único <see cref="Meter"/>
/// registrado como singleton, consumido por los handlers vía DI e inyectado en
/// <c>AddTaxVisionOpenTelemetry</c> por nombre para que el <c>MeterProvider</c> lo recolecte.
/// Los gauges (<c>mrr_usd</c>, <c>dunning.queue_depth</c>) son "calculated": se resuelven con
/// una query a demanda de cada scrape, no se acumulan en memoria — igual que cualquier gauge
/// respaldado por SQL.
/// </summary>
public sealed class PaymentAppMetrics : IPaymentAppMetrics, IDisposable
{
    public const string MeterName = "TaxVision.PaymentApp";

    private readonly Meter _meter;
    private readonly Counter<long> _attemptedTotal;
    private readonly Counter<long> _succeededTotal;
    private readonly Counter<long> _failedTotal;
    private readonly Counter<long> _refundedTotal;
    private readonly Counter<long> _chargebackTotal;
    private readonly Counter<long> _webhookReceivedTotal;
    private readonly Counter<long> _webhookDuplicateTotal;
    private readonly Counter<long> _webhookSignatureFailedTotal;
    private readonly Histogram<double> _providerLatencyMs;

    public PaymentAppMetrics(IServiceScopeFactory scopeFactory)
    {
        _meter = new Meter(MeterName);

        _attemptedTotal = _meter.CreateCounter<long>("paymentapp.saas_payments.attempted_total");
        _succeededTotal = _meter.CreateCounter<long>("paymentapp.saas_payments.succeeded_total");
        _failedTotal = _meter.CreateCounter<long>("paymentapp.saas_payments.failed_total");
        _refundedTotal = _meter.CreateCounter<long>("paymentapp.saas_payments.refunded_total");
        _chargebackTotal = _meter.CreateCounter<long>("paymentapp.saas_payments.chargeback_total");
        _webhookReceivedTotal = _meter.CreateCounter<long>("paymentapp.webhook.received_total");
        _webhookDuplicateTotal = _meter.CreateCounter<long>("paymentapp.webhook.duplicate_total");
        _webhookSignatureFailedTotal = _meter.CreateCounter<long>("paymentapp.webhook.signature_failed_total");
        _providerLatencyMs = _meter.CreateHistogram<double>("paymentapp.provider.latency_ms", unit: "ms");

        _meter.CreateObservableGauge("paymentapp.dunning.queue_depth", () => ComputeDunningQueueDepth(scopeFactory));
        _meter.CreateObservableGauge("paymentapp.mrr_usd", () => ComputeMrrUsd(scopeFactory));
    }

    public void RecordAttempted(string provider, string type) =>
        _attemptedTotal.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("type", type));

    public void RecordSucceeded(string provider, string type) =>
        _succeededTotal.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("type", type));

    public void RecordFailed(string provider, string type, string failureCode) =>
        _failedTotal.Add(
            1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("type", type),
            new KeyValuePair<string, object?>("failure_code", failureCode));

    public void RecordRefunded(string provider) => _refundedTotal.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordChargedBack(string provider) => _chargebackTotal.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordWebhookReceived(string provider) => _webhookReceivedTotal.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordWebhookDuplicate(string provider) => _webhookDuplicateTotal.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordWebhookSignatureFailed(string provider) =>
        _webhookSignatureFailedTotal.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordProviderLatency(double milliseconds, string provider, string method) =>
        _providerLatencyMs.Record(
            milliseconds, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("method", method));

    private static long ComputeDunningQueueDepth(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<ISaaSPaymentRepository>();
        return payments.CountDueForRetryAsync(DateTime.UtcNow, CancellationToken.None).GetAwaiter().GetResult();
    }

    /// <summary>Proxy de MRR, no un cálculo GAAP real — suma lo cobrado exitosamente en
    /// renovaciones de suscripción base en los últimos 30 días. PaymentApp no conoce el
    /// calendario de recurrencia real de cada plan (eso vive en Subscription), así que no
    /// puede calcular un MRR normalizado sin consultar a otro servicio.</summary>
    private static double ComputeMrrUsd(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var payments = scope.ServiceProvider.GetRequiredService<ISaaSPaymentRepository>();
        var cents = payments.SumSucceededAmountCentsAsync(SaaSPaymentType.SubscriptionRenewal, DateTime.UtcNow.AddDays(-30), CancellationToken.None)
            .GetAwaiter().GetResult();
        return cents / 100.0;
    }

    public void Dispose() => _meter.Dispose();
}
