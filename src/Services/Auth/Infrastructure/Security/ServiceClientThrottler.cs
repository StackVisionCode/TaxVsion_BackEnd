using BuildingBlocks.Infrastructure.RateLimiting;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Infrastructure.Security;

/// <summary>
/// Cuenta en Redis los fallos de credenciales del grant M2M por dirección, con ventana fija (como
/// <see cref="LoginThrottler"/>).
/// <list type="bullet">
/// <item>Por dirección y nunca por <c>clientId</c>: contar por cliente dejaría a cualquiera bloquear a
/// propósito el token de un servicio real.</item>
/// <item>Si Redis no responde, deja pasar: todos los servicios piden aquí su token y el grant no puede
/// caerse con Redis. La barrera principal es el Gateway.</item>
/// </list>
/// </summary>
public sealed class ServiceClientThrottler(
    IConnectionMultiplexer redis,
    IRateCounter rateCounter,
    ILogger<ServiceClientThrottler> logger
) : IServiceClientThrottler
{
    // Un servicio bien configurado nunca falla; un secreto mal puesto falla siempre y no necesita más intentos.
    public const int MaxFailuresPerAddress = 20;
    public static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(15);

    public async Task<TimeSpan?> GetRetryAfterAsync(string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return null;

        try
        {
            var key = FailureKey(ipAddress);
            var database = redis.GetDatabase();
            if ((long)await database.StringGetAsync(key.Value) < MaxFailuresPerAddress)
                return null;

            // Ventana fija: lo que falta es el TTL de la clave, no la ventana completa.
            return await database.KeyTimeToLiveAsync(key.Value) ?? FailureWindow;
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(
                "Service token throttle unavailable ({ExceptionType}); the grant is not blocked.",
                ex.GetType().Name
            );
            return null;
        }
    }

    public async Task RegisterFailureAsync(string? ipAddress, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ipAddress))
            return;

        try
        {
            await rateCounter.IncrementAndGetAsync(FailureKey(ipAddress), FailureWindow, ct);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning("Service token failure not counted ({ExceptionType}).", ex.GetType().Name);
        }
    }

    private static RateCounterKey FailureKey(string ipAddress) =>
        RateCounterKey.From($"auth:service-token-fail-ip:{ipAddress}");
}
