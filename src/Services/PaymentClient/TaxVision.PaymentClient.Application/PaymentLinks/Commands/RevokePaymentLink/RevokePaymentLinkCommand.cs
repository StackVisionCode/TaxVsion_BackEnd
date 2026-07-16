namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.RevokePaymentLink;

public sealed record RevokePaymentLinkCommand(Guid TenantId, Guid PaymentLinkId, string Reason, Guid ActorUserId);
