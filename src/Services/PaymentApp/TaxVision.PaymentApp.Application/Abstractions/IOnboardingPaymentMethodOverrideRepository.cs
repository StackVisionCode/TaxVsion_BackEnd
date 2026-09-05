using TaxVision.PaymentApp.Domain.PaymentMethods;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Application.Abstractions;

public interface IOnboardingPaymentMethodOverrideRepository
{
    Task<IReadOnlyList<OnboardingPaymentMethodOverride>> ListAsync(CancellationToken ct = default);

    Task<OnboardingPaymentMethodOverride?> GetAsync(
        PaymentProviderCode providerCode,
        string method,
        CancellationToken ct = default
    );

    Task AddAsync(OnboardingPaymentMethodOverride paymentMethodOverride, CancellationToken ct = default);
}
