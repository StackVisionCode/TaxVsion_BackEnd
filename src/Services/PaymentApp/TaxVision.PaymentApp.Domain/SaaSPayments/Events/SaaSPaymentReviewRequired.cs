using BuildingBlocks.Domain;

namespace TaxVision.PaymentApp.Domain.SaaSPayments.Events;

/// <summary>El proveedor confirmó un cobro que no se pudo aplicar: queda para revisión manual del admin.</summary>
public sealed record SaaSPaymentReviewRequired(
    Guid SaaSPaymentId,
    Guid TenantId,
    string ReasonCode,
    string? ProviderReference,
    long? PaidAmountCents,
    string? PaidCurrency,
    DateTime FlaggedAtUtc
) : IDomainEvent;
