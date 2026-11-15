using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace BuildingBlocks.Web.BotProtection;

/// <summary>Pieza opcional: la registra solo el servicio que tiene un formulario público con Turnstile.</summary>
public static class TurnstileRegistration
{
    /// <summary>Lee la configuración de <paramref name="section"/> (p. ej. "Calendar:PublicBooking:Turnstile").</summary>
    public static IServiceCollection AddTurnstileVerification(
        this IServiceCollection services,
        IConfigurationSection section
    )
    {
        services.Configure<TurnstileOptions>(section);
        services.AddHttpClient<ITurnstileVerifier, TurnstileVerifier>(
            (provider, client) =>
            {
                var seconds = provider.GetRequiredService<IOptions<TurnstileOptions>>().Value.TimeoutSeconds;
                client.Timeout = TimeSpan.FromSeconds(seconds is >= 1 and <= 30 ? seconds : 5);
            }
        );
        return services;
    }
}
