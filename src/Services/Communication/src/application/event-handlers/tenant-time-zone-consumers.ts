import { randomUUID } from 'node:crypto';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';
import type { TenantHostCache } from '../ports/tenant-host-cache.js';
import type { TenantTimeZoneStore } from '../ports/tenant-time-zone-store.js';
import { normalizeTimeZoneId, projectChange } from '../../domain/time-zones/tenant-time-zone.js';

/** Alias de los eventos de Tenant que traen la zona de la oficina (ver CLR_TYPE_TO_EVENT_TYPE en consumer-runtime). */
export const TenantTimeZoneEventTypes = {
  Created: 'tenant.created.v1',
  Updated: 'tenant.updated.v1',
  TimeZoneChanged: 'tenant.time_zone_changed.v1',
} as const;

/**
 * Proyecta la zona de cada oficina desde Tenant: el alta la siembra y cada cambio la reemplaza si es más nuevo.
 * Un cambio aplicado también olvida el host cacheado de esa oficina, que trae la zona vieja.
 */
export function bindTenantTimeZoneConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { store: TenantTimeZoneStore; hostCache?: TenantHostCache; emitter?: RealtimeEmitter },
): void {
  register(TenantTimeZoneEventTypes.Created, async (env) => {
    const tenantId = pickString(env.payload, 'newTenantId', 'NewTenantId');
    await project(deps, tenantId, env.payload, null, env.occurredOnUtc);
  });

  register(TenantTimeZoneEventTypes.Updated, async (env) => {
    const tenantId = pickString(env.payload, 'updatedTenantId', 'UpdatedTenantId');
    const version = pick(env.payload, 'tenantVersion', 'TenantVersion');
    const changedAt = pickString(env.payload, 'updatedAtUtc', 'UpdatedAtUtc') ?? env.occurredOnUtc;
    await project(deps, tenantId, env.payload, typeof version === 'number' ? version : null, changedAt);
  });

  // P27: la proyección la hace tenant.updated; este solo avisa a las sesiones abiertas para que recarguen el perfil.
  register(TenantTimeZoneEventTypes.TimeZoneChanged, async (env) => {
    const tenantId = pickString(env.payload, 'changedTenantId', 'ChangedTenantId') ?? env.tenantId;
    const timeZoneId = normalizeTimeZoneId(pick(env.payload, 'newTimeZoneId', 'NewTimeZoneId'));
    if (!deps.emitter || !tenantId || !timeZoneId) return;
    deps.emitter.emitToTenantMembers({
      tenantId,
      event: NotificationSocketEvents.OfficeTimeZoneChanged,
      envelope: { eventId: randomUUID(), correlationId: env.correlationId ?? '', emittedAtUtc: new Date().toISOString(), payload: { timeZoneId } },
    });
  });
}

async function project(
  deps: { store: TenantTimeZoneStore; hostCache?: TenantHostCache },
  tenantId: string | undefined,
  payload: Readonly<Record<string, unknown>>,
  version: number | null,
  changedAt: string | undefined,
): Promise<void> {
  const timeZoneId = normalizeTimeZoneId(pick(payload, 'defaultTimeZoneId', 'DefaultTimeZoneId'));
  const changedAtUtc = changedAt ? new Date(changedAt) : null;
  // Sin oficina, zona válida o fecha no hay nada que proyectar.
  if (!tenantId || !timeZoneId || !changedAtUtc || Number.isNaN(changedAtUtc.getTime())) return;

  const next = projectChange(await deps.store.find(tenantId), {
    tenantId,
    timeZoneId,
    sourceVersion: version,
    changedAtUtc,
  });
  if (!next) return;
  await deps.store.save(next);
  deps.hostCache?.forget(tenantId);
}

function pick(payload: Readonly<Record<string, unknown>>, camel: string, pascal: string): unknown {
  return payload[camel] ?? payload[pascal];
}

function pickString(payload: Readonly<Record<string, unknown>>, camel: string, pascal: string): string | undefined {
  const value = pick(payload, camel, pascal);
  return typeof value === 'string' && value.trim() ? value.trim() : undefined;
}
