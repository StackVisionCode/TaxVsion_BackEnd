import { logger } from '../logger/logger.js';
import { processMissedCalls } from '../../application/use-cases/process-missed-calls.js';
import type { CallRepository } from '../../application/ports/call-repository.js';
import type { IntegrationEventPublisher } from '../../application/ports/integration-event-publisher.js';

/**
 * Scheduler simple con setInterval: cada `intervalSeconds` revisa llamadas
 * Ringing con edad > `timeoutSeconds` y las marca MissedCall.
 *
 * Coordinacion multi-pod: usamos un lock Redis con TTL corto para que un solo
 * pod procese en cada tick. Sin lock, dos pods llegan al mismo aggregate y
 * uno pierde con guard-check del dominio — sigue siendo correcto, solo hace
 * trabajo redundante. En Fase 4 sustituimos este lock por el mismo pattern
 * NX PX Lua release que Signature usa para sealing.
 */
export interface MissedCallSchedulerConfig {
  readonly intervalSeconds: number;
  readonly ringingTimeoutSeconds: number;
}

export function startMissedCallScheduler(
  config: MissedCallSchedulerConfig,
  deps: { calls: CallRepository; publisher: IntegrationEventPublisher },
): { stop(): void } {
  const handle = setInterval(async () => {
    try {
      const { processed } = await processMissedCalls(
        { timeoutSeconds: config.ringingTimeoutSeconds },
        deps,
      );
      if (processed > 0) {
        logger.info({ processed }, 'MissedCallScheduler: processed missed calls');
      }
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
