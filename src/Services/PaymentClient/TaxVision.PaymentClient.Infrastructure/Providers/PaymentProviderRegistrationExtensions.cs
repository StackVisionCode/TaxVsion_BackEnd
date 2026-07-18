using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.PaymentClient.Application.Abstractions.Payments;

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
        var providerTypes = Assembly.GetExecutingAssembly().GetTypes()
            .Where(type => !type.IsAbstract && typeof(IPaymentProvider).IsAssignableFrom(type));

        foreach (var providerType in providerTypes)
        {
            var attribute = providerType.GetCustomAttribute<PaymentProviderAttribute>();
            if (attribute is null)
                continue;

            services.AddKeyedSingleton(typeof(IPaymentProvider), attribute.Code, providerType);
        }

        services.AddSingleton<IPaymentAdapterFactory, KeyedPaymentAdapterFactory>();
        return services;
    }
}
