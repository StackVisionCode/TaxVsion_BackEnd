using BuildingBlocks.Domain;

namespace TaxVision.PaymentApp.Domain.SaaSPayments.Events;

/// <summary>El intento de cobro falló. <see cref="SaaSPaymentSnapshot.NextRetryAtUtc"/> dice si el dunning lo reintenta.</summary>
public sealed record SaaSPaymentFailed(SaaSPaymentSnapshot Payment) : IDomainEvent;
