using BuildingBlocks.Domain;

namespace TaxVision.PaymentApp.Domain.SaaSPayments.Events;

/// <summary>
/// El pago se canceló. <paramref name="PropagatedFromOwner"/> es true cuando la cancelación la pidió el dueño del
/// cobro (Subscription cerró el intent o lo pagó por otro lado): él ya lo sabe y no se le anuncia nada.
/// </summary>
public sealed record SaaSPaymentCancelled(SaaSPaymentSnapshot Payment, bool PropagatedFromOwner) : IDomainEvent;
