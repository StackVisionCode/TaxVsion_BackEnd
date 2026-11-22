import { logger } from '../logger/logger.js';
import { processMissedMessages } from '../../application/use-cases/process-missed-messages.js';
import type { MissedMessagesStore } from '../../application/ports/missed-messages-store.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';

/** D-N4: cada minuto avisa de los mensajes que siguen sin leer. Un solo pod por tick (candado Redis). */
const LOCK_KEY = 'comm:lock:missed-messages-scheduler';

export function startMissedMessagesScheduler(
  config: { readonly intervalSeconds: number; readonly afterMinutes: number },
  deps: { readonly store: MissedMessagesStore; readonly lock: RedisDistributedLock },
): { stop(): void } {
  if (config.afterMinutes <= 0) return { stop: () => undefined };
  const handle = setInterval(async () => {
    try {
      await deps.lock.withLock(LOCK_KEY, Math.max(config.intervalSeconds * 3000, 5_000), async () => {
        const published = await processMissedMessages({ afterMinutes: config.afterMinutes }, deps);
        if (published > 0) logger.info({ published }, 'MissedMessagesScheduler: missed-message notices published');
      });
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'MissedMessagesScheduler tick failed');
    }
  }, config.intervalSeconds * 1000);
  return { stop: () => clearInterval(handle) };
}
