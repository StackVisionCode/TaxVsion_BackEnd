import { logger } from '../logger/logger.js';
import { sweepActiveCalls, type CallLifecycleSweepDeps } from '../../application/use-cases/call-lifecycle-sweep.js';
import { sweepAbandonedCalls, type AbandonedCallSweepDeps } from '../../application/use-cases/sweep-abandoned-calls.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';

/**
 * F4: aviso y corte por `maxCallMinutes`, mismo patron que el scheduler de reuniones (lock Redis, tick tecnico).
 * También cierra las llamadas abandonadas (abiertas sin nadie conectado, `abandonedGraceSeconds`).
 */
const LOCK_KEY = 'comm:lock:call-lifecycle-scheduler';

export function startCallLifecycleScheduler(
  config: { intervalSeconds: number; abandonedGraceSeconds: number },
  deps: CallLifecycleSweepDeps & AbandonedCallSweepDeps & { lock: RedisDistributedLock },
): { stop(): void } {
  const handle = setInterval(async () => {
    try {
      await deps.lock.withLock(LOCK_KEY, Math.max(config.intervalSeconds * 3000, 5_000), async () => {
        const report = await sweepActiveCalls({}, deps);
        if (report.warned + report.endedByTime > 0) {
          logger.info(report, 'CallLifecycleScheduler: tick applied changes');
        }
        const abandoned = await sweepAbandonedCalls({ graceSeconds: config.abandonedGraceSeconds }, deps);
        if (abandoned.abandoned > 0) {
          logger.info(abandoned, 'CallLifecycleScheduler: abandoned calls closed');
        }
      });
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'CallLifecycleScheduler tick failed');
    }
  }, config.intervalSeconds * 1000);
  return {
    stop() {
      clearInterval(handle);
    },
  };
}
