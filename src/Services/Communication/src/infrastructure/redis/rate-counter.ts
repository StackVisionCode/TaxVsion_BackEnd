import type { Redis } from 'ioredis';

/**
 * Rate Limit Fase 0.4 — INCR + EXPIRE-solo-en-el-primer-incremento como un unico
 * EVAL Lua, cerrando el hueco no atomico que tenian SocketRateLimiter y
 * DominantSpeakerThrottle (INCR y EXPIRE como dos llamadas Redis separadas: si el
 * proceso muere entre ambas, la clave queda sin TTL para siempre). Mismo patron
 * que RedisRateCounter.cs (BuildingBlocks.Infrastructure, F26) del lado .NET.
 */
const INCREMENT_SCRIPT = `
local count = redis.call('INCR', KEYS[1])
if count == 1 then
  redis.call('EXPIRE', KEYS[1], ARGV[1])
end
return count
`;

/** Incrementa `key` y devuelve el nuevo valor; fija el TTL a `windowSeconds` solo en el primer incremento del ciclo. */
export async function incrementAndGet(redis: Redis, key: string, windowSeconds: number): Promise<number> {
  const count = await redis.eval(INCREMENT_SCRIPT, 1, key, windowSeconds);
  return Number(count);
}

// Ventana fija que solo consume si hay cupo: un rechazo NO incrementa (antes un cliente que reintentaba
// bloqueado seguía bloqueado hasta dejar de insistir una ventana entera). Devuelve {permitido, ttl_ms}
// en la misma ida a Redis; el TTL es lo que falta de verdad, no la ventana completa. Una clave sin TTL
// (legacy) se re-expira en vez de bloquear para siempre.
const CONSUME_WITHIN_LIMIT_SCRIPT = `
local current = tonumber(redis.call('GET', KEYS[1]) or '0')
if current >= tonumber(ARGV[2]) then
  local ttl = redis.call('PTTL', KEYS[1])
  if ttl < 0 then
    redis.call('EXPIRE', KEYS[1], ARGV[1])
    ttl = tonumber(ARGV[1]) * 1000
  end
  return { 0, ttl }
end
local count = redis.call('INCR', KEYS[1])
if count == 1 then
  redis.call('EXPIRE', KEYS[1], ARGV[1])
end
return { 1, 0 }
`;

export async function consumeWithinLimit(
  redis: Redis,
  key: string,
  windowSeconds: number,
  maxPerWindow: number,
): Promise<{ allowed: boolean; ttlMs: number }> {
  const [allowed, ttlMs] = (await redis.eval(CONSUME_WITHIN_LIMIT_SCRIPT, 1, key, windowSeconds, maxPerWindow)) as [
    number,
    number,
  ];
  return { allowed: Number(allowed) === 1, ttlMs: Number(ttlMs) };
}
