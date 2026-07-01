using TaxVision.Payment.Application.Abstractions;
using TaxVision.Payment.Domain.TenantPayments;
using TaxVision.Payment.Infrastructure.Payments.Adapters;

namespace TaxVision.Payment.Infrastructure.Payments;

public sealed class PaymentAdapterFactory : IPaymentAdapterFactory
{
    private readonly StripePaymentAdapter _stripe;
    private readonly PayPalPaymentAdapter _payPal;

    public PaymentAdapterFactory(StripePaymentAdapter stripe, PayPalPaymentAdapter payPal)
    {
        _stripe = stripe;
        _payPal = payPal;
    }

    public IPaymentAdapter GetAdapter(TenantPaymentProvider provider) => provider switch
    {
        TenantPaymentProvider.Stripe => _stripe,
        TenantPaymentProvider.PayPal => _payPal,
        _ => throw new NotSupportedException($"Payment provider '{provider}' is not supported.")
    };
}
