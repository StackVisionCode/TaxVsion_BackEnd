namespace TaxVision.PaymentClient.Application.Admin.Queries;

/// <summary>Igual que <c>TenantPaymentResponse</c> pero con <see cref="TenantId"/> — el flujo
/// tenant-scoped no lo necesita, el admin cross-tenant sí.</summary>
public sealed record TenantPaymentAdminResponse(
    Guid Id,
    Guid TenantId,
    long AmountCents,
    string Currency,
    Guid? TaxpayerId,
    string PurposeKind,
    string? PurposeExternalReferenceId,
    string ProviderCode,
    string Status,
    string? ExternalChargeReference,
    string? FailureCode,
    string? FailureReason,
    DateTime? PaidAtUtc,
    DateTime CreatedAtUtc
);
