using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using BuildingBlocks.Caching;
using Microsoft.Extensions.Caching.Distributed;

namespace BuildingBlocks.Infrastructure.Caching;

/// <summary>
/// Cache distribuido sobre <see cref="IDistributedCache"/> (Redis en todos los entornos).
///
/// <para>
/// BB-07 — se evaluó migrar a <c>Microsoft.Extensions.Caching.Hybrid</c> y se descartó por tres
/// razones medidas, no de preferencia: (1) <c>HybridCache</c> no expone ningún get sin factory
/// —ni <c>GetAsync</c> ni <c>TryGetAsync</c>—, así que <see cref="GetAsync{T}"/> no tiene
/// traducción: emularlo con <c>GetOrCreateAsync</c> **escribiría** la entrada, y sobre la denylist
/// de sesiones eso convierte "no está denegada" en un valor cacheado; (2) su lectura es fail-open,
/// lo que revierte H-06 —la denylist tiene que propagar el fallo de Redis para que
/// <c>SessionDenylistMiddleware</c> aplique su política configurada—; y (3) su L1 in-process no
/// tiene backplane, así que una sesión revocada en Auth seguiría siendo válida en los otros 16
/// servicios hasta que expirara su copia local. Los defectos que motivaban el cambio están
/// corregidos acá abajo, que era el objetivo real del hallazgo.
/// </para>
/// </summary>
public sealed class RedisCacheService(IDistributedCache cache) : ICacheService
{
    /// <summary>
    /// Explícitas a propósito: con las opciones default, un cambio de default en una versión futura
    /// de .NET reinterpretaría en silencio todo lo ya escrito en Redis. Estos valores son los que
    /// producen el mismo payload que el serializador venía emitiendo — cambiar el naming acá dejaría
    /// ilegible lo cacheado por la versión anterior.
    /// </summary>
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false,
    };

    /// <summary>
    /// Coalescing de <see cref="GetOrCreateAsync{T}"/> por clave: sin esto, N peticiones concurrentes
    /// con la misma clave fría lanzan N factories contra el origen (una estampida). Es por proceso,
    /// no cross-servicio — con 17 servicios reduce la estampida al número de réplicas, no a 1.
    /// </summary>
    private static readonly ConcurrentDictionary<string, Task> InFlight = new();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var (found, value) = await TryReadAsync<T>(key, ct);
        return found ? value : default;
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var opt = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromMinutes(10),
        };
        return cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(value, SerializerOptions), opt, ct);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) => cache.RemoveAsync(key, ct);

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl = null,
        CancellationToken ct = default
    )
    {
        var (found, cached) = await TryReadAsync<T>(key, ct);
        if (found)
            return cached!;

        // La task en vuelo se comparte, pero cada caller espera con su propio token: si uno cancela,
        // los demás siguen. Se quita del diccionario al terminar (incluso al fallar) para no cachear
        // una excepción y dejar la clave envenenada.
        var inFlight =
            (Task<T>)InFlight.GetOrAdd(key, _ => CreateAndStoreAsync(key, factory, ttl, CancellationToken.None));

        try
        {
            return await inFlight.WaitAsync(ct);
        }
        finally
        {
            InFlight.TryRemove(new KeyValuePair<string, Task>(key, inFlight));
        }
    }

    private async Task<T> CreateAndStoreAsync<T>(
        string key,
        Func<CancellationToken, Task<T>> factory,
        TimeSpan? ttl,
        CancellationToken ct
    )
    {
        var value = await factory(ct);
        await SetAsync(key, value, ttl, ct);
        return value;
    }

    /// <summary>
    /// Decide hit/miss por **presencia de bytes**, no por si el valor deserializado es null. La
    /// versión anterior hacía <c>cached is not null</c> sobre el resultado de
    /// <see cref="GetAsync{T}"/>: con un <c>T</c> de tipo valor, un miss devuelve
    /// <c>default(T)</c> —<c>false</c>, <c>0</c>— que no es null, así que se reportaba como hit y la
    /// factory nunca corría. Simétricamente, un <c>null</c> legítimamente cacheado se trataba como
    /// miss y se recalculaba en cada lectura.
    /// </summary>
    private async Task<(bool Found, T? Value)> TryReadAsync<T>(string key, CancellationToken ct)
    {
        var bytes = await cache.GetAsync(key, ct);
        return bytes is null ? (false, default) : (true, JsonSerializer.Deserialize<T>(bytes, SerializerOptions));
    }
}
