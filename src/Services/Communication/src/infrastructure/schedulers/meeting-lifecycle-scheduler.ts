import { logger } from '../logger/logger.js';
import {
  expireStaleScheduledMeetings,
  sendDueMeetingReminders,
  sweepLiveMeetings,
  type LifecycleSweepDeps,
} from '../../application/use-cases/meeting-lifecycle-sweep.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';

/**
 * Ciclo de vida de reuniones (T-39): cada `intervalSeconds` avisa 10/5/1, corta al tope, termina las
 * salas vacías y expira las agendadas vencidas. Los plazos salen del resolver (global/oficina), el
 * tick es técnico. Lock Redis: un solo pod barre por tick, como los demás schedulers.
 */
const LOCK_KEY = 'comm:lock:meeting-lifecycle-scheduler';

export interface MeetingLifecycleSchedulerConfig {
  readonly intervalSeconds: number;
}

export function startMeetingLifecycleScheduler(
  config: MeetingLifecycleSchedulerConfig,
  deps: LifecycleSweepDeps & { lock: RedisDistributedLock },
): { stop(): void } {
  const handle = setInterval(async () => {
    try {
      await deps.lock.withLock(LOCK_KEY, Math.max(config.intervalSeconds * 3000, 5_000), async () => {
        const live = await sweepLiveMeetings({}, deps);
        const expiry = await expireStaleScheduledMeetings({}, deps);
        const reminders = await sendDueMeetingReminders({}, deps);
        if (live.warned + live.endedByTime + live.endedEmpty + expiry.expired + reminders.sent > 0) {
          logger.info({ ...live, expired: expiry.expired, reminders: reminders.sent }, 'MeetingLifecycleScheduler: tick applied changes');
        }
      });
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'MeetingLifecycleScheduler tick failed');
    }
  }, config.intervalSeconds * 1000);

  return {
    stop() {
      clearInterval(handle);
    },
  };
}
