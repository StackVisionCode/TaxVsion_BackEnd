namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>Nombres de las rutas HTTP que reciben los webhooks de cada proveedor (los usa el controller y el
/// descriptor del adaptador, así la URL que ve la oficina sale del enrutado y no se arma a mano).</summary>
public static class PaymentWebhookRoutes
{
    public const string Stripe = "StripeTenantWebhook";
    public const string PayPal = "PayPalTenantWebhook";
}
