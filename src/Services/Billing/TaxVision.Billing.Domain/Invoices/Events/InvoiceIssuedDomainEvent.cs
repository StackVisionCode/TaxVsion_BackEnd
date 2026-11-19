using BuildingBlocks.Domain;

namespace TaxVision.Billing.Domain.Invoices.Events;

/// <summary>
/// La factura se emitió (Draft → Issued). Lleva lo que publica InvoiceIssued; <see cref="IssuedByUserId"/> es
/// null si la emitió un proceso.
/// </summary>
public sealed record InvoiceIssuedDomainEvent(
    Guid InvoiceId,
    Guid TenantId,
    string InvoiceNumber,
    Guid CustomerId,
    long TotalAmountCents,
    string Currency,
    DateTime DueDateUtc,
    DateTime IssuedAtUtc,
    Guid? IssuedByUserId
) : IDomainEvent;
