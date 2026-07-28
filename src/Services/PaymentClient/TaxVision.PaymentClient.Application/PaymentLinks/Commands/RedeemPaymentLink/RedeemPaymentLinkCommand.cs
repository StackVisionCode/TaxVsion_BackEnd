using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;

/// <summary>Sin <c>TenantId</c> ni <c>ActorUserId</c> a propósito — el taxpayer no tiene JWT,
/// el tenant se deriva del <see cref="LinkToken"/> resuelto dentro del handler. Fase 2B:
/// <see cref="Provider"/> es el método que el taxpayer eligió entre los activos del tenant.</summary>
public sealed record RedeemPaymentLinkCommand(
    string LinkToken,
    PaymentProviderCode Provider,
    string ProviderPaymentMethodToken,
    string? ReceiptEmail
);
