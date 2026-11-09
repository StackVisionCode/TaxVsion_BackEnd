using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TaxVision.Sms.Application;
using TaxVision.Sms.Application.Abstractions;
using TaxVision.Sms.Application.Providers;

namespace TaxVision.Sms.Infrastructure.Providers;

/// <summary>Resuelve el adapter por su código vía keyed DI. El Application nunca hace switch(provider).</summary>
public sealed class KeyedSmsAdapterFactory(IServiceProvider serviceProvider) : ISmsAdapterFactory
{
    public ISmsProvider Resolve(string code) =>
        serviceProvider.GetKeyedService<ISmsProvider>(code)
        ?? throw new InvalidOperationException($"No ISmsProvider is registered for provider code '{code}'.");

    public bool TryResolve(string code, [NotNullWhen(true)] out ISmsProvider? provider)
    {
        provider = serviceProvider.GetKeyedService<ISmsProvider>(code);
        return provider is not null;
    }
}

/// <summary>Secreto de verificación de firma por proveedor (de su config). Sin resolver global.</summary>
public sealed class SmsWebhookSecrets(IOptions<SmsProvidersOptions> options) : ISmsWebhookSecrets
{
    public string? GetSecret(string providerCode) =>
        options.Value.Providers.TryGetValue(providerCode, out var config) ? config.Webhook.Secret : null;
}

/// <summary>Falla el arranque si el proveedor elegido no está registrado (por ejemplo, el fake apagado).</summary>
public sealed class SmsProviderSelectionValidator(IReadOnlySet<string> registeredCodes) : IValidateOptions<SmsOptions>
{
    public ValidateOptionsResult Validate(string? name, SmsOptions options)
    {
        // Mismo criterio que SmsProviderRouter: ProviderOrder manda; si está vacía, DefaultProvider.
        var order = (options.ProviderOrder ?? []).Where(code => !string.IsNullOrWhiteSpace(code)).ToList();
        var selected = order.Count > 0 ? order : [options.DefaultProvider];

        var missing = selected
            .Where(code => string.IsNullOrWhiteSpace(code) || !registeredCodes.Contains(code))
            .ToList();
        if (missing.Count == 0)
            return ValidateOptionsResult.Success;

        return ValidateOptionsResult.Fail(
            missing.Select(code =>
                $"SMS provider '{code}' is selected by Sms:DefaultProvider/Sms:ProviderOrder but is not registered. "
                + $"Test providers are registered only when Sms:Providers:{code}:Enabled is true."
            )
        );
    }
}

public static class SmsProviderRegistrationExtensions
{
    /// <summary>Descubre por reflexión toda clase <see cref="ISmsProvider"/> con <see cref="SmsProviderAttribute"/>
    /// y la registra como keyed-scoped por su código. Agregar un proveedor = clase + atributo, sin tocar esto.</summary>
    public static IServiceCollection AddSmsProviders(this IServiceCollection services, IConfiguration configuration)
    {
        var providerTypes = Assembly
            .GetExecutingAssembly()
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false } && typeof(ISmsProvider).IsAssignableFrom(t))
            .Select(t => (Type: t, Attribute: t.GetCustomAttribute<SmsProviderAttribute>()))
            .Where(x => x.Attribute is not null);

        var registered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (type, attribute) in providerTypes)
        {
            var enabledKey = $"{SmsProvidersOptions.SectionName}:Providers:{attribute!.Code}:Enabled";
            if (attribute.RequiresOptIn && !configuration.GetValue<bool>(enabledKey))
                continue;
            services.AddKeyedScoped(typeof(ISmsProvider), attribute.Code, type);
            registered.Add(attribute.Code);
        }

        services.AddSingleton<IValidateOptions<SmsOptions>>(new SmsProviderSelectionValidator(registered));
        services.AddOptions<SmsOptions>().ValidateOnStart();

        services.AddScoped<ISmsAdapterFactory, KeyedSmsAdapterFactory>();
        services.AddSingleton<ISmsWebhookSecrets, SmsWebhookSecrets>();
        return services;
    }
}
