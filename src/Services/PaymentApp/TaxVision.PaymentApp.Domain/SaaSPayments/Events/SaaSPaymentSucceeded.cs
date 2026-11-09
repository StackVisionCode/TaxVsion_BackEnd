using BuildingBlocks.Domain;

namespace TaxVision.PaymentApp.Domain.SaaSPayments.Events;

/// <summary>El cobro quedó confirmado (Processing/RequiresAction → Succeeded).</summary>
public sealed record SaaSPaymentSucceeded(SaaSPaymentSnapshot Payment) : IDomainEvent;
