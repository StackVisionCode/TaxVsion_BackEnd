using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.PaymentApp.Application.Abstractions.Payments;

namespace TaxVision.PaymentApp.Infrastructure.Providers;

/// <summary>
/// Auto-registro reflectivo de todo <see cref="IPaymentProvider"/> decorado con
/// <see cref="PaymentProviderAttribute"/> en este ensamblado. Agregar un provider nuevo
/// requiere únicamente crear la clase — cero cambios aquí (guardrail §44.4/§44.1 ley 3).
/// </summary>
public static class PaymentProviderRegistrationExtensions
{
    public static IServiceCollection AddPaymentProviders(this IServiceCollection services)
    {
        var providerTypes = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IPaymentProvider).IsAssignableFrom(type));

        foreach (var providerType in providerTypes)
        {
            var attribute = providerType.GetCustomAttribute<PaymentProviderAttribute>();
            if (attribute is null)
                continue;

            services.AddKeyedScoped(typeof(IPaymentProvider), attribute.Code, providerType);
        }

        // Scoped, no Singleton — los IPaymentProvider son AddKeyedScoped (Intellipay depende de
        // ICacheService, que es Scoped); una fábrica Singleton capturaría el IServiceProvider
        // raíz y no podría resolverlos ("Cannot resolve scoped service from root provider").
        services.AddScoped<IPaymentAdapterFactory, KeyedPaymentAdapterFactory>();
        return services;
    }
}
