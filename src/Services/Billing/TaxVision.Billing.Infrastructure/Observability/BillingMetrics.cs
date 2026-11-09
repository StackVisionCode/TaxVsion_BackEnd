using System.Diagnostics.Metrics;
using TaxVision.Billing.Application.Abstractions;

namespace TaxVision.Billing.Infrastructure.Observability;

/// <summary>Métricas de baja cardinalidad del servicio Billing. Nunca agregar tenant, invoice,
/// cliente o payment id como tags.</summary>
public sealed class BillingMetrics : IBillingMetrics, IDisposable
{
    public const string MeterName = "TaxVision.Billing";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _invoicesIssued;
    private readonly Counter<long> _invoicesPaid;
    private readonly Counter<long> _receiptsIssued;
    private readonly Counter<long> _overpayments;
    private readonly Counter<long> _paymentsRejected;
    private readonly Counter<long> _stockSyncInsufficient;

    public BillingMetrics()
    {
        _stockSyncInsufficient = _meter.CreateCounter<long>("billing.stock_sync_insufficient_total");
        _invoicesIssued = _meter.CreateCounter<long>("billing.invoices_issued_total");
        _invoicesPaid = _meter.CreateCounter<long>("billing.invoices_paid_total");
        _receiptsIssued = _meter.CreateCounter<long>("billing.receipts_issued_total");
        _overpayments = _meter.CreateCounter<long>("billing.invoice_overpayments_total");
        _paymentsRejected = _meter.CreateCounter<long>("billing.payments_rejected_total");
    }

    public void RecordInvoiceIssued() => _invoicesIssued.Add(1);

    public void RecordInvoicePaid(string method) =>
        _invoicesPaid.Add(1, new KeyValuePair<string, object?>("method", method));

    public void RecordReceiptIssued() => _receiptsIssued.Add(1);

    public void RecordOverpayment() => _overpayments.Add(1);

    public void RecordPaymentRejected(string reason) =>
        _paymentsRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordStockSyncInsufficient() => _stockSyncInsufficient.Add(1);

    public void Dispose() => _meter.Dispose();
}
