using BuildingBlocks.Platform;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Wolverine;

namespace BuildingBlocks.Web.Platform;

public static class PlatformSettingsRegistration
{
    /// <summary>
    /// Registra <see cref="IPlatformSettings"/> con su refresco desde Tenant. Va SIEMPRE junto a
    /// <see cref="ListenForPlatformSettings"/> en la config de Wolverine: el handler pide el provider
    /// concreto, y sin este registro el servicio arranca bien y revienta con el primer mensaje.
    /// </summary>
    public static IServiceCollection AddPlatformSettings(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<PlatformSettingsOptions>(configuration.GetSection(PlatformSettingsOptions.SectionName));
        services.AddSingleton<PlatformSettingsProvider>();
        services.AddSingleton<IPlatformSettings>(sp => sp.GetRequiredService<PlatformSettingsProvider>());
        services.AddHttpClient(
            PlatformSettingsRefreshService.HttpClientName,
            client => client.Timeout = TimeSpan.FromSeconds(10)
        );
        services.AddHostedService<PlatformSettingsRefreshService>();
        return services;
    }

    /// <summary>Suma el handler de <c>PlatformSettingsChangedIntegrationEvent</c> a los de este servicio.</summary>
    public static WolverineOptions ListenForPlatformSettings(this WolverineOptions options)
    {
        options.Discovery.IncludeType(typeof(PlatformSettingsChangedHandler));
        return options;
    }
}
