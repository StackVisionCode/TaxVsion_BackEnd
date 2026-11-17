namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// Volver al pago con otro proveedor (eligió PayPal, volvió atrás y eligió Stripe): el checkout abierto se cancela y se
/// abre otro con el proveedor nuevo. Con el mismo proveedor se reusa el abierto, que es lo que evita el doble cobro.
/// </summary>
public static class CheckoutProviderSwitch
{
    public const string Reason = "Payment method changed.";
}

/// <summary>El checkout que se canceló al cambiar de proveedor: el inicio cierra su sesión y avisa a PaymentApp.</summary>
public sealed record ReplacedCheckout(Guid IntentId, Guid PaymentId);
