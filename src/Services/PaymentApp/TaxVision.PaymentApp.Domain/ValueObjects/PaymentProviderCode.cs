namespace TaxVision.PaymentApp.Domain.ValueObjects;

/// <summary>
/// Catálogo de payment providers soportables por la plataforma. Los valores nunca se
/// reordenan ni se reutilizan tras eliminarse — son persistidos como string en BD
/// (<c>HasConversion&lt;string&gt;</c>), así que el enum en sí solo aporta type-safety en
/// código; el contrato real de compatibilidad es el nombre.
///
/// Fase A implementa adapters productivos para <see cref="Stripe"/>,
/// <see cref="Intellipay"/> y <see cref="Manual"/>. El resto queda reservado para
/// fases futuras — agregar un adapter nuevo nunca requiere insertar valores en medio
/// de este enum, solo agregar uno nuevo al final (ver guía de providers en el plan).
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
