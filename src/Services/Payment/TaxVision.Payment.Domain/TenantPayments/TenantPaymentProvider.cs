namespace TaxVision.Payment.Domain.TenantPayments;

public enum TenantPaymentProvider
{
    Stripe,
    PayPal,
    Square,
    MercadoPago,
    Manual
}
