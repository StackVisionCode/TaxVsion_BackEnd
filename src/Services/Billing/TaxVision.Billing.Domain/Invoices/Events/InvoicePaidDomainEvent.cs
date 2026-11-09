using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices.Events;

/// <summary>
/// La factura quedó pagada. Lleva lo que publica InvoicePaid, tomado al momento del pago.
/// <see cref="PaymentSource"/> es el origen del pago que la cubrió; null si la cubrió una edición.
/// </summary>
public sealed record InvoicePaidDomainEvent(
    Guid InvoiceId,
    Guid TenantId,
    string? InvoiceNumber,
    Guid CustomerId,
    long AmountPaidCents,
    string Currency,
    PaymentMethod PaymentMethod,
    DateTime? PaidAtUtc,
    Guid? PaidPdfFileId,
    InvoicePaymentSource? PaymentSource
) : IDomainEvent;
