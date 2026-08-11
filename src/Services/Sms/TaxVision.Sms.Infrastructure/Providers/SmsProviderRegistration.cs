using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Providers;

namespace TaxVision.Sms.Infrastructure.Providers;

/// <summary>Resuelve el adapter por su código vía keyed DI. El Application nunca hace switch(provider).</summary>
public sealed class KeyedSmsAdapterFactory(IServiceProvider serviceProvider) : ISmsAdapterFactory
{
    public ISmsProvider Resolve(string code) =>
        serviceProvider.GetKeyedService<ISmsProvider>(code)
        ?? throw new InvalidOperationException($"No ISmsProvider is registered for provider code '{code}'.");
}

/// <summary>Secreto de verificación de firma por proveedor (de su config). Sin resolver global.</summary>
public sealed class SmsWebhookSecrets(IOptions<SmsProvidersOptions> options) : ISmsWebhookSecrets
{
    public string? GetSecret(string providerCode) =>
        options.Value.Providers.TryGetValue(providerCode, out var config) ? config.Webhook.Secret : null;
}

public static class SmsProviderRegistrationExtensions
{
    /// <summary>Descubre por reflexión toda clase <see cref="ISmsProvider"/> con <see cref="SmsProviderAttribute"/>
    /// y la registra como keyed-scoped por su código. Agregar un proveedor = clase + atributo, sin tocar esto.</summary>
    public static IServiceCollection AddSmsProviders(this IServiceCollection services)
    {
        var providerTypes = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ISmsProvider).IsAssignableFrom(t))
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<SmsProviderAttribute>()))
            .Where(x => x.Attribute is not null);

        foreach (var (type, attribute) in providerTypes)
            services.AddKeyedScoped(typeof(ISmsProvider), attribute!.Code, type);

        services.AddScoped<ISmsAdapterFactory, KeyedSmsAdapterFactory>();
        services.AddSingleton<ISmsWebhookSecrets, SmsWebhookSecrets>();
        return services;
    }
}
