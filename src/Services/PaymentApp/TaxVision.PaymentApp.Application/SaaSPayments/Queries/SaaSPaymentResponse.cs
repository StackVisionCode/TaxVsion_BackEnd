namespace TaxVision.PaymentApp.Application.SaaSPayments.Queries;

public sealed record SaaSPaymentResponse(
    Guid Id,
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
