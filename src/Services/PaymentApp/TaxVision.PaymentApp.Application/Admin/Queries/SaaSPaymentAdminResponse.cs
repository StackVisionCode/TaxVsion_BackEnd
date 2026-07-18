namespace TaxVision.PaymentApp.Application.Admin.Queries;

/// <summary>Igual que <c>SaaSPaymentResponse</c> pero con <see cref="TenantId"/> — el flujo
/// tenant-scoped no lo necesita (el caller ya sabe su propio tenant), el admin cross-tenant
/// sí.</summary>
public sealed record SaaSPaymentAdminResponse(
    Guid Id,
    Guid TenantId,
    string Status,
    string Type,
    long AmountCents,
    string Currency,
    string ProviderCode,
    string? ExternalChargeReference,
    string? FailureCode,
    string? FailureReason,
    DateTime? NextRetryAtUtc,
    DateTime? PaidAtUtc,
    DateTime CreatedAtUtc
);
