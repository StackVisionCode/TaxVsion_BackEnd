import { Redis } from 'ioredis';
import { config } from '../config.js';
import { logger } from '../logger.js';
import { waitForRedis } from './wait-for-redis.js';

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

// Sin Redis el arranque espera en vez de morir; ioredis reintenta con retryStrategy.
export async function connectRedis(): Promise<void> {
  await waitForRedis(redis);
}

export async function disconnectRedis(): Promise<void> {
  await redis.quit();
}
