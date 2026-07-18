namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.CreatePaymentLink;

public sealed record CreatePaymentLinkResponse(Guid Id, string Token, DateTime ExpiresAtUtc);
