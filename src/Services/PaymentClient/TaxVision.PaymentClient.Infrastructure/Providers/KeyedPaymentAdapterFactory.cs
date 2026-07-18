using Microsoft.Extensions.DependencyInjection;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Infrastructure.Providers;

/// <summary>Resuelve un <see cref="IPaymentProvider"/> vía DI keyed. El registro de cada key
/// ocurre por reflexión en <c>PaymentProviderRegistrationExtensions</c> — esta clase no conoce
/// ningún provider concreto.</summary>
public sealed class KeyedPaymentAdapterFactory(IServiceProvider serviceProvider) : IPaymentAdapterFactory
{
    public IPaymentProvider Resolve(PaymentProviderCode code) =>
        serviceProvider.GetKeyedService<IPaymentProvider>(code)
        ?? throw new InvalidOperationException($"No IPaymentProvider is registered for provider code '{code}'.");
}
