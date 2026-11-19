using BuildingBlocks.Domain;
using TaxVision.Billing.Domain.ValueObjects;

namespace TaxVision.Billing.Domain.Invoices.Events;

/// <summary>
/// La factura cambió de estado (cualquier transición auditada: emisión, cobro, anulación, edición, cambio manual o
/// corrección de estado). Se levanta junto a la fila de <see cref="InvoiceStatusChange"/>, así que no hay cambio sin
/// aviso. Lo traduce el handler a <c>billing.invoice.status_changed.v1</c> (outbox, después del commit).
/// </summary>
public sealed record InvoiceStatusChangedDomainEvent(
    Guid InvoiceId,
    Guid TenantId,
    string? InvoiceNumber,
    InvoiceStatus? FromStatus,
    InvoiceStatus ToStatus,
    string Trigger,
    Guid ChangedByUserId,
    DateTime ChangedAtUtc
) : IDomainEvent;
