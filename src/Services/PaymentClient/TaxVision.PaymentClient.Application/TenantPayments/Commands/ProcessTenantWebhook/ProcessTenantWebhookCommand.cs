using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.ProcessTenantWebhook;

/// <summary><paramref name="Headers"/>: encabezados de firma que el adaptador del proveedor necesita
/// (Stripe: <c>Stripe-Signature</c>; PayPal: los <c>PAYPAL-*</c>).</summary>
public sealed record ProcessTenantWebhookCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    string RawPayload,
    IReadOnlyDictionary<string, string> Headers
);
