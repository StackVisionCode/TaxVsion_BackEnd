using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Billing.Abstractions;
using TaxVision.PaymentApp.Domain.Billing;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>Cómo se cobra una renovación. Con <see cref="FallbackReason"/> no hay cobro off-session posible.</summary>
public sealed record ChargeStrategy(
    PaymentProviderCode? ProviderCode,
    string? PaymentMethodToken,
    string? FallbackReason
)
{
    public static ChargeStrategy OffSession(PaymentProviderCode code, string token) => new(code, token, null);

    public static ChargeStrategy Fallback(string reason) => new(null, null, reason);
}

/// <summary>Elige la estrategia de cobro de una renovación. El contrato que garantiza NO cobrar sin mandate vive acá:
/// un mandate revocado o un provider sin registrar fuerzan el fallback manual.</summary>
public static class ChargeStrategySelector
{
    public static async Task<ChargeStrategy> SelectAsync(
        Guid tenantId,
        TenantBillingPreferenceProjection? preference,
        ITenantProviderCustomerRepository customers,
        IOffSessionChargerFactory offSessionFactory,
        CancellationToken ct
    )
    {
        if (preference is null)
            return ChargeStrategy.Fallback("No billing preference projected");

        if (!preference.AllowsOffSessionCharge)
            return ChargeStrategy.Fallback(
                preference.Mode == TenantBillingPreferenceProjection.ModeManual
                    ? "Preference is Manual"
                    : "No active mandate or provider"
            );

        if (!Enum.TryParse<PaymentProviderCode>(preference.ProviderCode!, ignoreCase: true, out var provider))
            return ChargeStrategy.Fallback($"Unknown provider code '{preference.ProviderCode}'");

        if (offSessionFactory.For(provider.ToString()) is null)
            return ChargeStrategy.Fallback($"No IOffSessionCharger registered for {provider}");

        var providerCustomer = await customers.GetByTenantAndProviderAsync(tenantId, provider, ct);
        if (providerCustomer is null)
            return ChargeStrategy.Fallback("No TenantProviderCustomer for this tenant/provider");

        var defaultMethod = providerCustomer.GetDefaultMethod();
        if (defaultMethod is null)
            return ChargeStrategy.Fallback("No default saved payment method");

        return ChargeStrategy.OffSession(provider, defaultMethod.MethodReference);
    }
}
