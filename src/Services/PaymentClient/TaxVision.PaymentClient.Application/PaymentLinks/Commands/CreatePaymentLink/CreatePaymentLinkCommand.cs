using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.CreatePaymentLink;

public sealed record CreatePaymentLinkCommand(
    Guid TenantId,
    Guid? TaxpayerId,
    long AmountCents,
    string Currency,
    PaymentPurposeKind PurposeKind,
    string? PurposeExternalReferenceId,
    TimeSpan Expiration,
    Guid ActorUserId
);
