import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { LimitsRepository } from '../ports/settings-repository.js';
import { applyLimitsUpdate } from '../use-cases/settings-use-cases.js';

/**
 * Consumers Subscription -> proyeccion `TenantCommunicationLimits`. Cierra
 * la deuda del plan: hasta ahora los limits se hidrataban con defaults
 * conservadores (max 4 participants, etc). Ahora reflejan el plan real.
 */
export function bindSubscriptionConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { limits: LimitsRepository },
): void {
  register('subscription.activated.v1', async (env) => {
    const snapshot = extractLimitsSnapshot(env);
    if (!snapshot) return;
    await applyLimitsUpdate({ ...snapshot, isSuspended: false }, deps);
  });

  register('subscription.plan_changed.v1', async (env) => {
    const snapshot = extractLimitsSnapshot(env);
    if (!snapshot) return;
    await applyLimitsUpdate({ ...snapshot, isSuspended: false }, deps);
  });

  register('subscription.seats_purchased.v1', async (env) => {
    const snapshot = extractLimitsSnapshot(env);
    if (!snapshot) return;
    await applyLimitsUpdate({ ...snapshot, isSuspended: false }, deps);
  });

  register('subscription.suspended.v1', async (env) => {
    await deps.limits.markSuspended(env.tenantId, true, new Date());
  });
}

function extractLimitsSnapshot(env: IncomingEnvelope): {
  tenantId: string;
  planCode: string;
  maxMeetingParticipants: number;
  maxMeetingMinutes: number;
  maxConcurrentCalls: number;
  maxMonthlyMinutes: number;
  recordingEnabled: boolean;
  supportEnabled: boolean;
  isSuspended: boolean;
  updatedAtUtc: Date;
} | null {
  const planCode = getString(env.payload, 'planCode', 'PlanCode');
  if (!planCode) return null;
  return {
    tenantId: env.tenantId,
    planCode,
    maxMeetingParticipants: getNumber(env.payload, 'maxMeetingParticipants', 'MaxMeetingParticipants') ?? 4,
    maxMeetingMinutes: getNumber(env.payload, 'maxMeetingMinutes', 'MaxMeetingMinutes') ?? 60,
    maxConcurrentCalls: getNumber(env.payload, 'maxConcurrentCalls', 'MaxConcurrentCalls') ?? 2,
    maxMonthlyMinutes: getNumber(env.payload, 'maxMonthlyMinutes', 'MaxMonthlyMinutes') ?? 600,
    recordingEnabled: getBool(env.payload, 'recordingEnabled', 'RecordingEnabled') ?? false,
    supportEnabled: getBool(env.payload, 'supportEnabled', 'SupportEnabled') ?? true,
    isSuspended: false,
    updatedAtUtc: new Date(),
  };
}

function getString(src: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const k of keys) {
    const v = src[k];
    if (typeof v === 'string') return v;
  }
  return undefined;
}
function getNumber(src: Record<string, unknown>, ...keys: string[]): number | undefined {
  for (const k of keys) {
    const v = src[k];
    if (typeof v === 'number') return v;
    if (typeof v === 'string') {
      const p = Number.parseInt(v, 10);
      if (Number.isFinite(p)) return p;
    }
  }
  return undefined;
}
function getBool(src: Record<string, unknown>, ...keys: string[]): boolean | undefined {
  for (const k of keys) {
    const v = src[k];
    if (typeof v === 'boolean') return v;
    if (typeof v === 'string') {
      if (v === 'true') return true;
      if (v === 'false') return false;
    }
  }
  return undefined;
}
