namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>URL pública del webhook de una oficina: base de <see cref="PaymentWebhookOptions"/> + la ruta
/// nombrada del descriptor resuelta por el enrutado (la implementa la API). Null si no se puede armar.</summary>
public interface IPaymentWebhookUrlBuilder
{
    Uri? Build(string webhookRouteName, Guid tenantId);
}
