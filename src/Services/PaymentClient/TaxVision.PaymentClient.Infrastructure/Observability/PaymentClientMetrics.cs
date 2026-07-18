using System.Diagnostics.Metrics;
using TaxVision.PaymentClient.Application.Abstractions;

namespace TaxVision.PaymentClient.Infrastructure.Observability;

/// <summary>
/// Catálogo de métricas custom de PaymentClient (§29.2 del diseño) — un único
/// <see cref="Meter"/> registrado como singleton, inyectado en <c>AddTaxVisionOpenTelemetry</c>
/// por nombre para que el <c>MeterProvider</c> lo recolecte.
/// </summary>
public sealed class PaymentClientMetrics : IPaymentClientMetrics, IDisposable
{
    public const string MeterName = "TaxVision.PaymentClient";

    private readonly Meter _meter;
    private readonly Counter<long> _gmvCents;
    private readonly Counter<long> _paymentCountTotal;
    private readonly Counter<long> _platformFeeCentsTotal;
    private readonly Counter<long> _connectOnboardingCompletedTotal;
    private readonly Counter<long> _paymentLinksCreatedTotal;
    private readonly Counter<long> _paymentLinksUsedTotal;
    private readonly Counter<long> _refundTotal;
    private readonly Counter<long> _webhookReceivedTotal;
    private readonly Counter<long> _webhookDuplicateTotal;
    private readonly Counter<long> _webhookSignatureFailedTotal;

    public PaymentClientMetrics()
    {
        _meter = new Meter(MeterName);

        _gmvCents = _meter.CreateCounter<long>("paymentclient.tenant_payments.gmv_cents");
        _paymentCountTotal = _meter.CreateCounter<long>("paymentclient.tenant_payments.count_total");
        _platformFeeCentsTotal = _meter.CreateCounter<long>("paymentclient.platform_fee_cents_total");
        _connectOnboardingCompletedTotal = _meter.CreateCounter<long>(
            "paymentclient.connect.onboarding_completed_total"
        );
        _paymentLinksCreatedTotal = _meter.CreateCounter<long>("paymentclient.payment_links.created_total");
        _paymentLinksUsedTotal = _meter.CreateCounter<long>("paymentclient.payment_links.used_total");
        _refundTotal = _meter.CreateCounter<long>("paymentclient.refund_total");
        _webhookReceivedTotal = _meter.CreateCounter<long>("paymentclient.webhook.received_total");
        _webhookDuplicateTotal = _meter.CreateCounter<long>("paymentclient.webhook.duplicate_total");
        _webhookSignatureFailedTotal = _meter.CreateCounter<long>("paymentclient.webhook.signature_failed_total");
    }

    public void RecordPaymentSucceeded(long amountCents, string currency)
    {
        _gmvCents.Add(amountCents, new KeyValuePair<string, object?>("currency", currency));
        _paymentCountTotal.Add(1);
    }

    public void RecordPlatformFee(long feeCents, string currency) =>
        _platformFeeCentsTotal.Add(feeCents, new KeyValuePair<string, object?>("currency", currency));

    public void RecordConnectOnboardingCompleted() => _connectOnboardingCompletedTotal.Add(1);

    public void RecordPaymentLinkCreated() => _paymentLinksCreatedTotal.Add(1);

    public void RecordPaymentLinkUsed() => _paymentLinksUsedTotal.Add(1);

    public void RecordRefund(string provider) =>
        _refundTotal.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordWebhookReceived(string provider) =>
        _webhookReceivedTotal.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordWebhookDuplicate(string provider) =>
        _webhookDuplicateTotal.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void RecordWebhookSignatureFailed(string provider) =>
        _webhookSignatureFailedTotal.Add(1, new KeyValuePair<string, object?>("provider", provider));

    public void Dispose() => _meter.Dispose();
}
