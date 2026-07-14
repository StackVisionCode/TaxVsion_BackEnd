import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { LimitsRepository } from '../ports/settings-repository.js';
import { applyLimitsUpdate } from '../use-cases/settings-use-cases.js';

/**
 * Consumer Subscription -> proyeccion `TenantCommunicationLimits`.
 *
 * Escucha el evento unico `subscription.entitlements_changed.v1` (reemplaza a los
 * cuatro antiguos activated/plan_changed/seats_purchased/suspended, retirados en la
 * fase de cleanup del rediseno de Subscription). El payload trae `entitlementValues`,
 * el snapshot resuelto completo (key -> valor stringificado) en el mismo instante del
 * recalculo — no hace falta un round-trip a Subscription para leerlo.
 *
 * Ningun plan define todavia claves `communication.*` (el catalogo actual solo siembra
 * `module.*` y limites core como `seats.max`/`storage.max_bytes`), asi que estas
 * lecturas caen a los defaults conservadores de siempre hasta que ese catalogo se
 * extienda — mismo comportamiento que el consumer anterior.
 */
export function bindSubscriptionConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { limits: LimitsRepository },
): void {
  register('subscription.entitlements_changed.v1', async (env) => {
    const snapshot = extractLimitsSnapshot(env);
    if (!snapshot) return;
    await applyLimitsUpdate(snapshot, deps);
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

  const entitlementValues = getEntitlementValues(env.payload);
  const subscriptionStatus = getString(env.payload, 'subscriptionStatus', 'SubscriptionStatus');

  return {
    tenantId: env.tenantId,
    planCode,
    maxMeetingParticipants: getEntitlementNumber(entitlementValues, 'communication.max_participants_per_meeting') ?? 4,
    maxMeetingMinutes: getEntitlementNumber(entitlementValues, 'communication.max_meeting_minutes') ?? 60,
    maxConcurrentCalls: getEntitlementNumber(entitlementValues, 'communication.max_concurrent_calls') ?? 2,
    maxMonthlyMinutes: getEntitlementNumber(entitlementValues, 'communication.max_monthly_minutes') ?? 600,
    recordingEnabled: getEntitlementBool(entitlementValues, 'communication.recording_enabled') ?? false,
    supportEnabled: getEntitlementBool(entitlementValues, 'communication.support_chat_enabled') ?? true,
    isSuspended: subscriptionStatus === 'Suspended',
    updatedAtUtc: new Date(),
  };
}

function getEntitlementValues(payload: Record<string, unknown>): Record<string, string> {
  const raw = payload['entitlementValues'] ?? payload['EntitlementValues'];
  return raw && typeof raw === 'object' ? (raw as Record<string, string>) : {};
}

function getEntitlementNumber(entitlementValues: Record<string, string>, key: string): number | undefined {
  const raw = entitlementValues[key];
  if (raw === undefined) return undefined;
  const parsed = Number.parseInt(raw, 10);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function getEntitlementBool(entitlementValues: Record<string, string>, key: string): boolean | undefined {
  const raw = entitlementValues[key];
  if (raw === 'True' || raw === 'true') return true;
  if (raw === 'False' || raw === 'false') return false;
  return undefined;
}

function getString(src: Record<string, unknown>, ...keys: string[]): string | undefined {
  for (const k of keys) {
    const v = src[k];
    if (typeof v === 'string') return v;
  }
  return undefined;
}
