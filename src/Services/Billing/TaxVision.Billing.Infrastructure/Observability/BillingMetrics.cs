using System.Diagnostics.Metrics;

namespace TaxVision.Billing.Infrastructure.Observability;

/// <summary>Métricas de baja cardinalidad del servicio Billing. Nunca agregar tenant, invoice,
/// cliente o payment id como tags.</summary>
public sealed class BillingMetrics : IDisposable
{
    public const string MeterName = "TaxVision.Billing";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _invoicesIssued;
    private readonly Counter<long> _invoicesPaid;
    private readonly Counter<long> _receiptsIssued;

    public BillingMetrics()
    {
        _invoicesIssued = _meter.CreateCounter<long>("billing.invoices_issued_total");
        _invoicesPaid = _meter.CreateCounter<long>("billing.invoices_paid_total");
        _receiptsIssued = _meter.CreateCounter<long>("billing.receipts_issued_total");
    }

    public void RecordInvoiceIssued() => _invoicesIssued.Add(1);

    public void RecordInvoicePaid(string method) =>
        _invoicesPaid.Add(1, new KeyValuePair<string, object?>("method", method));

    public void RecordReceiptIssued() => _receiptsIssued.Add(1);

    public void Dispose() => _meter.Dispose();
}
