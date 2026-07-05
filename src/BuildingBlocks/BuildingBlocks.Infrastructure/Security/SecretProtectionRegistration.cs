using BuildingBlocks.Security;
using Microsoft.Extensions.DependencyInjection;

namespace BuildingBlocks.Infrastructure.Security;

public static class SecretProtectionRegistration
{
    /// <summary>
    /// Registra <see cref="ISecretProtector"/> (AES-256-GCM, clave <c>Encryption:MasterKey</c>)
    /// como singleton. Es idempotente: no reemplaza un protector ya registrado por el servicio.
    /// </summary>
    public static IServiceCollection AddSecretProtection(this IServiceCollection services)
    {
        services.AddSingleton<ISecretProtector, AesGcmSecretProtector>();
        return services;
    }
}
