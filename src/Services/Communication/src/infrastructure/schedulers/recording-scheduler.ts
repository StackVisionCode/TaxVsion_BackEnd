import { logger } from '../logger/logger.js';
import {
  runRecordingRetention,
  runRecordingSweep,
  type RecordingRetentionDeps,
  type RecordingSweepDeps,
} from '../../application/use-cases/recording-sweep.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';

/**
 * F7: barrido de grabaciones cada `intervalSeconds` (limite, disponibilidad, composicion colgada) y
 * retencion cada `retentionIntervalMinutes`. Lock Redis: un solo pod por tick, como los demas schedulers.
 */
const LOCK_KEY = 'comm:lock:recording-scheduler';
const RETENTION_LOCK_KEY = 'comm:lock:recording-retention';

export interface RecordingSchedulerConfig {
  readonly intervalSeconds: number;
  readonly retentionIntervalMinutes: number;
}

export function startRecordingScheduler(
  config: RecordingSchedulerConfig,
  deps: RecordingSweepDeps & RecordingRetentionDeps & { lock: RedisDistributedLock },
): { stop(): void } {
  const sweep = setInterval(async () => {
    try {
      await deps.lock.withLock(LOCK_KEY, Math.max(config.intervalSeconds * 3000, 5_000), async () => {
        const report = await runRecordingSweep(new Date(), deps);
        if (report.warned + report.stoppedByLimit + report.ready + report.failed > 0) {
          logger.info(report, 'RecordingScheduler: tick applied changes');
        }
      });
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'RecordingScheduler tick failed');
    }
  }, config.intervalSeconds * 1000);

  const retention = setInterval(async () => {
    try {
      await deps.lock.withLock(RETENTION_LOCK_KEY, 10 * 60_000, async () => {
        const report = await runRecordingRetention(new Date(), deps);
        if (report.tracks + report.recordings > 0) logger.info(report, 'Recording retention purged files');
      });
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'Recording retention tick failed');
    }
  }, config.retentionIntervalMinutes * 60_000);

  return {
    stop() {
      clearInterval(sweep);
      clearInterval(retention);
    },
  };
}
