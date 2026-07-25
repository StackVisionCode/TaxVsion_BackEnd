using TaxVision.Billing.Domain.Invoices;
using TaxVision.Billing.Domain.Receipts;

namespace TaxVision.Billing.Application.Abstractions;

/// <summary>Acceso a facturas del tenant. SCAFFOLD B1: se implementa en B2.</summary>
public interface IInvoiceRepository
{
    Task<Invoice?> GetByIdAsync(Guid tenantId, Guid invoiceId, CancellationToken ct = default);
    Task AddAsync(Invoice invoice, CancellationToken ct = default);
}

/// <summary>Acceso a comprobantes de pago. SCAFFOLD B1: se implementa en B3.</summary>
public interface IPaymentReceiptRepository
{
    Task<PaymentReceipt?> GetByIdAsync(Guid tenantId, Guid receiptId, CancellationToken ct = default);
    Task AddAsync(PaymentReceipt receipt, CancellationToken ct = default);
}
