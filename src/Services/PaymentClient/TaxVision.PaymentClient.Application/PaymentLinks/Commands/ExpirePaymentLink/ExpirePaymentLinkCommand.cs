namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.ExpirePaymentLink;

public sealed record ExpirePaymentLinkCommand(Guid TenantId, Guid PaymentLinkId);
