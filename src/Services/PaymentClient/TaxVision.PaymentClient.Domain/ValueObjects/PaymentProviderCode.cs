namespace TaxVision.PaymentClient.Domain.ValueObjects;

/// <summary>
/// Catálogo de payment providers soportables por el marketplace. Mismo valor de enum que
/// <c>TaxVision.PaymentApp.Domain.ValueObjects.PaymentProviderCode</c> — deliberadamente un
/// tipo distinto, no compartido vía referencia de ensamblado: PaymentClient y PaymentApp son
/// microservicios separados con sus propias bases de datos y ciclos de despliegue.
/// </summary>
public enum PaymentProviderCode
{
    Stripe = 1,
    Intellipay = 2,
    Manual = 3,
    PayPal = 4,
    Braintree = 5,
    Adyen = 6,
    Chargebee = 7,
    Paddle = 8,
    Square = 9,
    Klarna = 10,
    MercadoPago = 11,
    WeChatPay = 12,
    Alipay = 13,
    GoCardless = 14,
    Razorpay = 15,
    AuthorizeNet = 16,
}
