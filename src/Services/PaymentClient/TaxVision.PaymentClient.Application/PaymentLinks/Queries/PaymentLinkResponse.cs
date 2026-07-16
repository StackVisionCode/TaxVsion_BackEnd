namespace TaxVision.PaymentClient.Application.PaymentLinks.Queries;

public sealed record PaymentLinkResponse(
    Guid Id,
    Guid? TaxpayerId,
    long AmountCents,
    string Currency,
    string PurposeKind,
    string? PurposeExternalReferenceId,
    string Token,
    string Status,
    DateTime ExpiresAtUtc,
    DateTime CreatedAtUtc,
    DateTime? UsedAtUtc,
    Guid? RelatedTenantPaymentId
);
