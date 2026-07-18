using Microsoft.Extensions.DependencyInjection;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Infrastructure.Providers;

/// <summary>Resuelve un <see cref="IPaymentProvider"/> vía DI keyed. El registro de cada key
/// ocurre por reflexión en <c>PaymentProviderRegistrationExtensions</c> — esta clase no conoce
/// ningún provider concreto.</summary>
public sealed class KeyedPaymentAdapterFactory(IServiceProvider serviceProvider) : IPaymentAdapterFactory
{
    public IPaymentProvider Resolve(PaymentProviderCode code) =>
        serviceProvider.GetKeyedService<IPaymentProvider>(code)
        ?? throw new InvalidOperationException($"No IPaymentProvider is registered for provider code '{code}'.");
}
