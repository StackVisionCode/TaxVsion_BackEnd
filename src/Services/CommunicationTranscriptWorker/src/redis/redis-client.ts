import { Redis } from 'ioredis';
import { config } from '../config.js';
import { logger } from '../logger.js';

/**
 * Una sola conexion general — a diferencia de Communication (que necesita
 * pub/sub dedicados para el adapter de Socket.IO), este worker solo usa
 * Redis para el inbox de idempotencia.
 */
export const redis = new Redis(config.redis.uri, {
  lazyConnect: true,
  maxRetriesPerRequest: 3,
  connectionName: 'communication-transcript-worker',
  retryStrategy: (times) => {
    const delay = Math.min(1000 * Math.pow(2, times - 1), 15_000);
    logger.warn({ attempt: times, delayMs: delay }, 'Redis retrying');
    return delay;
  },
});

redis.on('connect', () => logger.info('Redis connected'));
redis.on('error', (err: Error) => logger.error({ err: err.message }, 'Redis error'));
redis.on('close', () => logger.warn('Redis connection closed'));

export async function connectRedis(): Promise<void> {
  await redis.connect();
}

export async function disconnectRedis(): Promise<void> {
  await redis.quit();
}
