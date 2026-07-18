namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;

public sealed record RedeemPaymentLinkResponse(
    Guid TenantPaymentId,
    string Status,
    string? NextActionType,
    string? NextActionUrl,
    string? FailureCode,
    string? FailureMessage
);
