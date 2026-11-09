using BuildingBlocks.Domain;

namespace TaxVision.PaymentClient.Domain.TenantPayments.Events;

/// <summary>
/// El pago llegó a <see cref="PaymentStatus.Succeeded"/>, por cualquier camino (cobro, webhook, reconciliación o
/// monto cero). Lleva la hora del pago; el resto del aviso se relee del pago.
/// </summary>
public sealed record TenantPaymentSucceededDomainEvent(Guid TenantId, Guid TenantPaymentId, DateTime PaidAtUtc)
    : IDomainEvent;
