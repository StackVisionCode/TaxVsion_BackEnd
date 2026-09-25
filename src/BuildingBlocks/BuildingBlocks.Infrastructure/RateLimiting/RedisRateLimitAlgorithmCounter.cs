using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using StackExchange.Redis;

namespace BuildingBlocks.Infrastructure.RateLimiting;

/// <summary>
/// Implementación de referencia de <see cref="IRateLimitAlgorithmCounter"/> — un script Lua
/// (<c>EVAL</c> atómico, sin round-trips intermedios) por algoritmo. Solo cubre los algoritmos con
/// consumidores reales hoy (ver auditoría post-Fase-9, hallazgo #8): <see cref="RateLimitAlgorithm.LeakyBucket"/>
/// no tiene ninguna política ruteada por este evaluador (las 2 únicas políticas K que lo declaran
/// son atendidas por <c>IProviderRateLimiter</c>, nunca por <see cref="TieredRateLimitEvaluator"/> —
/// mismo criterio "dormido" que <c>RateLimitPartitionDimension.AccountOrProvider</c>) — lanza en vez
/// de fingir una implementación sin tráfico real que la audite.
///
/// <para>
/// Cada script devuelve <c>{excedido, espera_ms}</c>. Un rechazo no consume cupo: antes la ventana fija
/// y la deslizante registraban también los intentos rechazados, así que un cliente que reintentaba
/// estando bloqueado se mantenía bloqueado hasta dejar de insistir una ventana entera. La espera es la
/// real (TTL restante, vencimiento de la entrada más vieja o tiempo al próximo token), no la ventana.
/// </para>
/// </summary>
public sealed class RedisRateLimitAlgorithmCounter(IConnectionMultiplexer redis) : IRateLimitAlgorithmCounter
{
    // Cada script arranca descartando la clave si tiene otro tipo de Redis: si una política cambia de
    // algoritmo (p.ej. ventana deslizante → token bucket) la clave vieja daría WRONGTYPE hasta vencer.
    //
    // Chequea antes de INCR; PEXPIRE solo en el primer incremento, igual criterio atómico que
    // RedisRateCounter (F26). Una clave sin TTL (legacy) se re-expira en vez de bloquear para siempre.
    private const string FixedWindowScript =
        @"
local kind = redis.call('TYPE', KEYS[1])['ok']
if kind ~= 'none' and kind ~= 'string' then
    redis.call('DEL', KEYS[1])
end
local current = tonumber(redis.call('GET', KEYS[1]) or '0')
if current >= tonumber(ARGV[2]) then
    local ttl = redis.call('PTTL', KEYS[1])
    if ttl < 0 then
        redis.call('PEXPIRE', KEYS[1], ARGV[1])
        ttl = tonumber(ARGV[1])
    end
    return {1, ttl}
end
local count = redis.call('INCR', KEYS[1])
if count == 1 then
    redis.call('PEXPIRE', KEYS[1], ARGV[1])
end
return {0, 0}";

    // Log de timestamps en un sorted set: poda lo que cayó fuera de la ventana, agrega el hit
    // actual (member único generado en C# — Lua no tiene una fuente de aleatoriedad segura para
    // colisiones bajo concurrencia real), cuenta lo que queda. TIME de Redis (no ARGV) para que el
    // reloj sea el del servidor, no el de cada réplica de la app. TIME devuelve {segundos,
    // microsegundos} como 2 elementos separados — combinar ambos es obligatorio: truncar a solo
    // segundos (descartando microsegundos) deja "now_ms" congelado durante ~1s enteros y saltando
    // 1000ms de golpe al cruzar el borde, con efectos correctos acá (ZCARD sigue siendo exacto) pero
    // catastróficos en TokenBucketScript (ver ahí) — se combinan los 2 componentes en ambos scripts
    // por consistencia aunque acá el bug real no aplique. El hit solo se agrega si hay cupo; la espera es
    // lo que falta para que venza la entrada más vieja (la próxima en liberar un lugar).
    private const string SlidingWindowScript =
        @"
local kind = redis.call('TYPE', KEYS[1])['ok']
if kind ~= 'none' and kind ~= 'zset' then
    redis.call('DEL', KEYS[1])
end
local time = redis.call('TIME')
local now_ms = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)
local window = tonumber(ARGV[1])
redis.call('ZREMRANGEBYSCORE', KEYS[1], '-inf', now_ms - window)
if redis.call('ZCARD', KEYS[1]) >= tonumber(ARGV[3]) then
    local oldest = redis.call('ZRANGE', KEYS[1], 0, 0, 'WITHSCORES')
    local retry = window
    if oldest[2] then
        retry = math.max(1, tonumber(oldest[2]) + window - now_ms)
    end
    return {1, retry}
end
redis.call('ZADD', KEYS[1], now_ms, ARGV[2])
redis.call('PEXPIRE', KEYS[1], window)
return {0, 0}";

    // Bucket clásico: capacidad = limit, refill continuo a limit/window tokens por ms. Estado en un
    // hash (tokens, ts_ms) — se relee y refilla en cada llamada antes de intentar consumir 1 token.
    // Precisión de microsegundos en "now_ms" es OBLIGATORIA acá (a diferencia de arriba): truncar a
    // segundos enteros regala hasta "limit" tokens de refill cada vez que 2 llamadas cruzan un borde
    // de segundo del reloj — aunque el tiempo real transcurrido haya sido de 1ms. Bug real detectado
    // con redis-cli manual antes de cerrar este hallazgo (ver hallazgo #8): con esa versión, un test
    // de integración de 61 requests reales nunca disparaba el 429 esperado.
    private const string TokenBucketScript =
        @"
local kind = redis.call('TYPE', KEYS[1])['ok']
if kind ~= 'none' and kind ~= 'hash' then
    redis.call('DEL', KEYS[1])
end
local capacity = tonumber(ARGV[1])
local refill_per_ms = tonumber(ARGV[2])
local ttl_ms = tonumber(ARGV[3])
local time = redis.call('TIME')
local now_ms = tonumber(time[1]) * 1000 + math.floor(tonumber(time[2]) / 1000)

local bucket = redis.call('HMGET', KEYS[1], 'tokens', 'ts')
local tokens = tonumber(bucket[1])
local ts = tonumber(bucket[2])
if tokens == nil then
    tokens = capacity
    ts = now_ms
end

local elapsed = math.max(0, now_ms - ts)
tokens = math.min(capacity, tokens + elapsed * refill_per_ms)

local exceeded = 0
local retry_ms = 0
if tokens >= 1 then
    tokens = tokens - 1
else
    exceeded = 1
    retry_ms = math.ceil((1 - tokens) / refill_per_ms)
end

redis.call('HMSET', KEYS[1], 'tokens', tokens, 'ts', now_ms)
redis.call('PEXPIRE', KEYS[1], ttl_ms)
return {exceeded, retry_ms}";

    public async Task<RateLimitCounterResult> EvaluateAsync(
        RateCounterKey key,
        RateLimitAlgorithm algorithm,
        int limit,
        TimeSpan window,
        CancellationToken ct = default
    )
    {
        // BB-13 — el token se honra acá y no más allá a propósito: **ningún** comando de
        // StackExchange.Redis acepta CancellationToken (todos los overloads terminan en
        // CommandFlags); la librería controla el corte por timeout de configuración, no por token.
        // Sin esta línea el parámetro era puramente decorativo. No intentar "propagarlo": no existe
        // el overload.
        ct.ThrowIfCancellationRequested();

        var db = redis.GetDatabase();
        var windowMs = (long)window.TotalMilliseconds;

        RedisResult result = algorithm switch
        {
            RateLimitAlgorithm.FixedWindow => await db.ScriptEvaluateAsync(
                    FixedWindowScript,
                    new RedisKey[] { key.Value },
                    new RedisValue[] { windowMs, limit }
                )
                .ConfigureAwait(false),
            RateLimitAlgorithm.SlidingWindow => await db.ScriptEvaluateAsync(
                    SlidingWindowScript,
                    new RedisKey[] { key.Value },
                    new RedisValue[] { windowMs, Guid.NewGuid().ToString("N"), limit }
                )
                .ConfigureAwait(false),
            RateLimitAlgorithm.TokenBucket => await db.ScriptEvaluateAsync(
                    TokenBucketScript,
                    new RedisKey[] { key.Value },
                    new RedisValue[] { limit, (double)limit / windowMs, windowMs }
                )
                .ConfigureAwait(false),
            RateLimitAlgorithm.LeakyBucket => throw new NotSupportedException(
                $"RedisRateLimitAlgorithmCounter no soporta LeakyBucket — la clave '{key.Value}' "
                    + "declara una política de categoría K, que se evalúa vía IProviderRateLimiter, "
                    + "nunca vía TieredRateLimitEvaluator (ver doc-comment de esta clase)."
            ),
            _ => throw new ArgumentOutOfRangeException(
                nameof(algorithm),
                algorithm,
                "Algoritmo de rate-limit desconocido."
            ),
        };

        var values = (RedisResult[])result!;
        if ((long)values[0] != 1)
            return RateLimitCounterResult.Allowed;

        return RateLimitCounterResult.Rejected(TimeSpan.FromMilliseconds(Math.Max(1, (long)values[1])));
    }
}
