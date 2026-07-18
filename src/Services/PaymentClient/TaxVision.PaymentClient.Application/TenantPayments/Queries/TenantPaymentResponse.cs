namespace TaxVision.PaymentClient.Application.TenantPayments.Queries;

public sealed record TenantPaymentResponse(
    Guid Id,
    long AmountCents,
    string Currency,
    Guid? TaxpayerId,
    string PurposeKind,
    string? PurposeExternalReferenceId,
    string ProviderCode,
    string Status,
    string? ExternalChargeReference,
    string? ProviderChargeReferenceOnConnect,
    long? TenantAmountCents,
    long? PlatformFeeAmountCents,
    string? NextActionType,
    string? NextActionUrl,
    string? FailureCode,
    string? FailureReason,
    DateTime? PaidAtUtc
);
