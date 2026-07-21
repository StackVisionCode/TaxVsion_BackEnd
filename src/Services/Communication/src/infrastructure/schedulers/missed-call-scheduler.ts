import { logger } from '../logger/logger.js';
import { processMissedCalls } from '../../application/use-cases/process-missed-calls.js';
import type { CallRepository } from '../../application/ports/call-repository.js';
import type { IntegrationEventPublisher } from '../../application/ports/integration-event-publisher.js';
import type { PresenceService } from '../../application/ports/presence-service.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';

/**
 * Scheduler simple con setInterval: cada `intervalSeconds` revisa llamadas
 * Ringing con edad > `timeoutSeconds` y las marca MissedCall.
 *
 * Coordinacion multi-pod: `RedisDistributedLock` (NX PX + Lua release) para
 * que un solo pod procese en cada tick. Sin el lock, dos pods llegan al mismo
 * aggregate y uno pierde con guard-check del dominio — sigue siendo correcto,
 * solo hace trabajo redundante; el lock evita ese trabajo duplicado.
 */
const LOCK_KEY = 'comm:lock:missed-call-scheduler';

export interface MissedCallSchedulerConfig {
  readonly intervalSeconds: number;
  readonly ringingTimeoutSeconds: number;
}

export function startMissedCallScheduler(
  config: MissedCallSchedulerConfig,
  deps: {
    calls: CallRepository;
    publisher: IntegrationEventPublisher;
    presence: PresenceService;
    lock: RedisDistributedLock;
  },
): { stop(): void } {
  const handle = setInterval(async () => {
    try {
      await deps.lock.withLock(LOCK_KEY, Math.max(config.intervalSeconds * 3000, 5_000), async () => {
        const { processed } = await processMissedCalls(
          { timeoutSeconds: config.ringingTimeoutSeconds },
          deps,
        );
        if (processed > 0) {
          logger.info({ processed }, 'MissedCallScheduler: processed missed calls');
        }
      });
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'MissedCallScheduler tick failed');
    }
  }, config.intervalSeconds * 1000);

  return {
    stop() {
      clearInterval(handle);
    },
  };
}
