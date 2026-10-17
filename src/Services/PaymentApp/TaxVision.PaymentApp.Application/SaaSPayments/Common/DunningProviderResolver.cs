using TaxVision.PaymentApp.Domain.Billing;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Common;

/// <summary>Resuelve el provider con el que un consumer de dunning (renovación de suscripción, seat o
/// addon) debe armar el <c>ChargeSaaSPaymentCommand</c>. Antes era hardcode a Stripe en los 3 consumers,
/// lo que fallaba eternamente para tenants que onboardearon con PayPal (no existe su customer en Stripe).
/// Hoy respeta el <c>ProviderCode</c> proyectado del <c>TenantBillingPreference</c>; solo cae a Stripe
/// cuando no hay proyección o el código no se puede parsear.</summary>
public static class DunningProviderResolver
{
    public static PaymentProviderCode Resolve(TenantBillingPreferenceProjection? preference)
    {
        if (preference is null || string.IsNullOrWhiteSpace(preference.ProviderCode))
            return PaymentProviderCode.Stripe;

        return Enum.TryParse<PaymentProviderCode>(preference.ProviderCode, ignoreCase: true, out var code)
            ? code
            : PaymentProviderCode.Stripe;
    }
}
