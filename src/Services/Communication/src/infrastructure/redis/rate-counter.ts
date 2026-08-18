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
