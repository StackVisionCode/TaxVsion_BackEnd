import { Redis, type RedisOptions } from 'ioredis';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';

/**
 * Factory de conexiones Redis. Comunicacion necesita al menos DOS conexiones
 * dedicadas para el adapter de Socket.IO (pub y sub) y una tercera para el uso
 * "generico" (presence, rate limit, cache, session denylist read).
 *
 * Cierre CRIT-13 del legacy: NO usamos enableOfflineQueue:false silente. Si
 * Redis se cae, el reconnect estrategia expone el estado via logs estructurados
 * y las operaciones fallan con Result<T, RedisUnavailableError> — nada de fallback
 * a Map local que crea divergencia entre pods.
 */
const baseOptions: RedisOptions = {
  lazyConnect: true,
  maxRetriesPerRequest: 3,
  reconnectOnError: (err) => {
    logger.warn({ err: err.message }, 'Redis reconnect on error');
    return true;
  },
  retryStrategy: (times) => {
    const delay = Math.min(1000 * Math.pow(2, times - 1), 15_000);
    logger.warn({ attempt: times, delayMs: delay }, 'Redis retrying');
    return delay;
  },
};

function createConnection(role: 'general' | 'pub' | 'sub'): Redis {
  const client = new Redis(config.redis.uri, {
    ...baseOptions,
    connectionName: `communication:${role}`,
  });
  client.on('connect', () => logger.info({ role }, 'Redis connected'));
  client.on('error', (err: Error) => logger.error({ role, err: err.message }, 'Redis error'));
  client.on('close', () => logger.warn({ role }, 'Redis connection closed'));
  return client;
}

export const redis = createConnection('general');
export const redisPub = createConnection('pub');
export const redisSub = createConnection('sub');

export async function connectRedis(): Promise<void> {
  await Promise.all([redis.connect(), redisPub.connect(), redisSub.connect()]);
}

export async function disconnectRedis(): Promise<void> {
  await Promise.all([redis.quit(), redisPub.quit(), redisSub.quit()]);
}
