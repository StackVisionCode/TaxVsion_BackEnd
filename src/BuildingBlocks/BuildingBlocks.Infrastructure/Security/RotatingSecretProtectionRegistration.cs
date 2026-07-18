using BuildingBlocks.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Security;

public static class RotatingSecretProtectionRegistration
{
    /// <summary>
    /// Registra <see cref="IRotatingSecretProtector"/> (AES-256-GCM con rotación de 2 keys) como
    /// singleton, leyendo la sección "Encryption" de <paramref name="configuration"/>.
    /// </summary>
    public static IServiceCollection AddRotatingSecretProtection(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.Configure<RotatingSecretProtectionOptions>(configuration.GetSection("Encryption"));
        services.AddSingleton<IRotatingSecretProtector, AesGcmRotatingSecretProtector>();
        return services;
    }
}
