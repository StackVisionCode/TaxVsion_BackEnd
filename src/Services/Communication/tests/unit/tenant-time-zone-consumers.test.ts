import { describe, expect, it } from 'vitest';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { TenantHostCache } from '../../src/application/ports/tenant-host-cache.js';
import type { TenantHostResolver } from '../../src/application/ports/tenant-host-resolver.js';
import type { TenantTimeZoneStore } from '../../src/application/ports/tenant-time-zone-store.js';
import {
  TenantTimeZoneEventTypes,
  bindTenantTimeZoneConsumers,
} from '../../src/application/event-handlers/tenant-time-zone-consumers.js';
import { ProjectedTimeZoneHostResolver } from '../../src/application/services/projected-time-zone-host-resolver.js';
import type { TenantTimeZone } from '../../src/domain/time-zones/tenant-time-zone.js';

const T = 'b7c1e0a2-0000-4000-8000-000000000002';

class InMemoryZones implements TenantTimeZoneStore {
  readonly rows = new Map<string, TenantTimeZone>();
  saves = 0;
  async find(tenantId: string): Promise<TenantTimeZone | null> {
    return this.rows.get(tenantId) ?? null;
  }
  async save(zone: TenantTimeZone): Promise<void> {
    this.saves++;
    this.rows.set(zone.tenantId, zone);
  }
}

class RecordingHostCache implements TenantHostCache {
  readonly forgotten: string[] = [];
  forget(tenantId: string): void {
    this.forgotten.push(tenantId);
  }
  forgetAll(): void {}
}

function bind(store: TenantTimeZoneStore, cache: TenantHostCache) {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  bindTenantTimeZoneConsumers((type, handler) => handlers.set(type, handler), { store, hostCache: cache });
  return (type: string) => {
    const handler = handlers.get(type);
    if (!handler) throw new Error(`no handler for ${type}`);
    return handler;
  };
}

function updated(zone: string, version: number | null, at: string): IncomingEnvelope {
  return {
    eventId: `e-${zone}-${version}`,
    eventType: TenantTimeZoneEventTypes.Updated,
    tenantId: T,
    occurredOnUtc: at,
    payload: { updatedTenantId: T, name: 'Office', defaultTimeZoneId: zone, updatedAtUtc: at, tenantVersion: version },
  };
}

describe('tenant time zone consumers', () => {
  it('el alta de la oficina siembra su zona', async () => {
    const store = new InMemoryZones();
    const handlerFor = bind(store, new RecordingHostCache());

    await handlerFor(TenantTimeZoneEventTypes.Created)({
      eventId: 'c1',
      eventType: TenantTimeZoneEventTypes.Created,
      tenantId: T,
      occurredOnUtc: '2026-11-14T09:00:00Z',
      payload: { NewTenantId: T, Name: 'Office', DefaultTimeZoneId: 'America/La_Paz' },
    });

    expect(store.rows.get(T)).toMatchObject({ timeZoneId: 'America/La_Paz', sourceVersion: null });
  });

  it('un cambio de zona se proyecta y olvida el host cacheado; uno más viejo no lo pisa', async () => {
    const store = new InMemoryZones();
    const cache = new RecordingHostCache();
    const handlerFor = bind(store, cache);
    const handle = handlerFor(TenantTimeZoneEventTypes.Updated);

    await handle(updated('America/Chicago', 3, '2026-11-14T10:46:25Z'));
    await handle(updated('Pacific/Honolulu', 2, '2026-11-14T10:45:49Z'));

    expect(store.rows.get(T)).toMatchObject({ timeZoneId: 'America/Chicago', sourceVersion: 3 });
    expect(store.saves).toBe(1);
    expect(cache.forgotten).toEqual([T]);
  });

  it('una zona inválida no toca la proyección', async () => {
    const store = new InMemoryZones();
    const handlerFor = bind(store, new RecordingHostCache());

    await handlerFor(TenantTimeZoneEventTypes.Updated)(updated('Not/AZone', 9, '2026-11-14T10:00:00Z'));

    expect(store.rows.size).toBe(0);
  });
});

describe('projected time zone host resolver', () => {
  const inner: TenantHostResolver = {
    resolveHost: async () => 'office.taxoffix.io',
    resolveTimeZone: async () => 'America/Santo_Domingo',
  };

  it('la zona sale de la proyección aunque Auth no tenga host; el host sigue en Auth', async () => {
    const store = new InMemoryZones();
    await store.save({ tenantId: T, timeZoneId: 'America/Chicago', sourceVersion: 3, sourceUpdatedAtUtc: new Date() });
    const resolver = new ProjectedTimeZoneHostResolver(store, { resolveHost: async () => null });

    expect(await resolver.resolveTimeZone(T)).toBe('America/Chicago');
    expect(await resolver.resolveHost(T)).toBeNull();
  });

  it('sin fila usa el pull a Auth como respaldo, y si la base falla también', async () => {
    const failing: TenantTimeZoneStore = {
      find: async () => {
        throw new Error('db down');
      },
      save: async () => {},
    };

    expect(await new ProjectedTimeZoneHostResolver(new InMemoryZones(), inner).resolveTimeZone(T)).toBe(
      'America/Santo_Domingo',
    );
    expect(await new ProjectedTimeZoneHostResolver(failing, inner).resolveTimeZone(T)).toBe('America/Santo_Domingo');
  });
});

// P27: cuando la oficina cambia de zona, las sesiones abiertas de sus miembros se enteran por el socket.
describe('tenant.time_zone_changed — aviso a las sesiones', () => {
  it('avisa a los miembros de la oficina con la zona nueva', async () => {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const sent: Array<{ tenantId: string; event: string; payload: unknown }> = [];
    bindTenantTimeZoneConsumers((type, handler) => handlers.set(type, handler), {
      store: new InMemoryZones(),
      emitter: {
        emitToTenantMembers: (input: { tenantId: string; event: string; envelope: { payload: unknown } }) =>
          void sent.push({ tenantId: input.tenantId, event: input.event, payload: input.envelope.payload }),
      } as never,
    });

    await handlers.get(TenantTimeZoneEventTypes.TimeZoneChanged)?.({
      eventId: 'e-zone',
      eventType: TenantTimeZoneEventTypes.TimeZoneChanged,
      tenantId: T,
      occurredOnUtc: '2026-11-15T15:00:00Z',
      payload: { ChangedTenantId: T, PreviousTimeZoneId: 'America/New_York', NewTimeZoneId: 'America/Chicago' },
    });

    expect(sent).toEqual([{ tenantId: T, event: 'office.time_zone.changed', payload: { timeZoneId: 'America/Chicago' } }]);
  });
});
