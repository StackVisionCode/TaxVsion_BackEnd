using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Infrastructure.Providers;

/// <summary>
/// Auto-registro reflectivo de todo <see cref="IPaymentProvider"/> decorado con
/// <see cref="PaymentProviderAttribute"/> en este ensamblado. Agregar un provider nuevo
/// requiere únicamente crear la clase — cero cambios aquí. Cada adapter se registra
/// <c>Singleton</c>: no guarda credenciales de ningún tenant en estado de instancia, las
/// recibe por parámetro en cada llamada.
/// </summary>
public static class PaymentProviderRegistrationExtensions
{
    public static IServiceCollection AddPaymentProviders(this IServiceCollection services)
    {
        // Algunos adapters (p.ej. PayPal) hacen HTTP server-to-server y necesitan IHttpClientFactory.
        // Idempotente: registrarlo de nuevo no rompe si ya estaba.
        services.AddHttpClient();
        services.TryAddSingleton<IStripeClientFactory>(StripeClientFactory.Default);

        var providerTypes = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IPaymentProvider).IsAssignableFrom(type));

        var registeredCodes = new List<PaymentProviderCode>();
        foreach (var providerType in providerTypes)
        {
            var attribute = providerType.GetCustomAttribute<PaymentProviderAttribute>();
            if (attribute is null)
                continue;

            services.AddKeyedSingleton(typeof(IPaymentProvider), attribute.Code, providerType);
            registeredCodes.Add(attribute.Code);
        }

        // La fábrica conoce los códigos registrados para exponer el catálogo sin enumerar DI.
        services.AddSingleton<IPaymentAdapterFactory>(provider => new KeyedPaymentAdapterFactory(
            provider,
            registeredCodes
        ));
        return services;
    }
}
