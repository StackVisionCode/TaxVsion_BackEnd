namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;

/// <summary>Sin <c>TenantId</c> ni <c>ActorUserId</c> a propósito — el taxpayer no tiene JWT,
/// el tenant se deriva del <see cref="LinkToken"/> resuelto dentro del handler.</summary>
public sealed record RedeemPaymentLinkCommand(string LinkToken, string ProviderPaymentMethodToken, string? ReceiptEmail);
