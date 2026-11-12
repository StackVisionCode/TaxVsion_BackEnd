using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using StackExchange.Redis;

namespace BuildingBlocks.Infrastructure.Redis;

/// <summary>
/// Conexión a Redis común a los servicios .NET: Redis caído no tumba ni el arranque ni las peticiones.
///
/// <para>
/// Antes cada servicio hacía <c>ConnectionMultiplexer.Connect(cadena)</c> con los valores por defecto: sin Redis la
/// fábrica del singleton lanzaba, no quedaba registrado y cada pedido volvía a esperar segundos para fallar igual.
/// </para>
///
/// <para>
/// Ahora <c>AbortOnConnectFail = false</c> deja el multiplexer creado y reconectando solo, y
/// <c>BacklogPolicy.FailFast</c> hace que sin conexión cada comando falle al instante en vez de esperar 5 s. Cada
/// uso decide su respaldo: la caché va al origen, el rate limit deja pasar, la denylist aplica su política y los
/// candados de jobs no corren ese ciclo (un cobro nunca corre sin candado).
/// </para>
///
/// <para>No registra chequeo de salud: la readiness de cada servicio sigue siendo la suya.</para>
/// </summary>
public static class RedisConnectionRegistration
{
    /// <summary>Opciones a partir de la cadena del servicio (respeta clave, SSL y demás).</summary>
    public static ConfigurationOptions CreateOptions(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.BacklogPolicy = BacklogPolicy.FailFast;
        return options;
    }

    /// <summary>Un solo <see cref="IConnectionMultiplexer"/> por servicio, aunque lo pidan Program.cs y la Infrastructure.</summary>
    public static IServiceCollection AddTaxVisionRedis(this IServiceCollection services, string connectionString)
    {
        var options = CreateOptions(connectionString);
        services.TryAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(options.Clone()));
        return services;
    }
}
