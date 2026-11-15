import { describe, expect, it } from 'vitest';
import type { TenantTimeZoneStore } from '../../src/application/ports/tenant-time-zone-store.js';
import type { TenantTimeZone } from '../../src/domain/time-zones/tenant-time-zone.js';
import {
  reconcileTenantTimeZonesOnce,
  type TenantDirectoryZoneRow,
} from '../../src/infrastructure/schedulers/tenant-time-zone-reconciliation-scheduler.js';

class InMemoryZones implements TenantTimeZoneStore {
  readonly rows = new Map<string, TenantTimeZone>();
  async find(tenantId: string): Promise<TenantTimeZone | null> {
    return this.rows.get(tenantId) ?? null;
  }
  async save(zone: TenantTimeZone): Promise<void> {
    this.rows.set(zone.tenantId, zone);
  }
}

function directory(rows: TenantDirectoryZoneRow[]) {
  const calls: number[] = [];
  return {
    calls,
    async listPage(page: number, size: number): Promise<TenantDirectoryZoneRow[] | null> {
      calls.push(page);
      return rows.slice((page - 1) * size, page * size);
    },
  };
}

describe('tenant time zone reconciliation', () => {
  it('siembra las oficinas que faltan, corrige las distintas y recorre todas las páginas', async () => {
    const store = new InMemoryZones();
    store.rows.set('t-2', {
      tenantId: 't-2',
      timeZoneId: 'America/Santo_Domingo',
      sourceVersion: 1,
      sourceUpdatedAtUtc: new Date('2026-11-01T00:00:00Z'),
    });
    const client = directory([
      { tenantId: 't-1', timeZoneId: 'America/New_York' },
      { tenantId: 't-2', timeZoneId: 'America/Chicago' },
      { tenantId: 't-3', timeZoneId: 'Etc/UTC' },
      { tenantId: 't-4', timeZoneId: 'Bogus/Zone' },
    ]);

    const outcome = await reconcileTenantTimeZonesOnce({ client, store }, 3);

    expect(outcome).toEqual({ applied: 3, completed: true });
    expect(client.calls).toEqual([1, 2]);
    expect(store.rows.get('t-1')?.timeZoneId).toBe('America/New_York');
    expect(store.rows.get('t-2')).toMatchObject({ timeZoneId: 'America/Chicago', sourceVersion: 1 });
    expect(store.rows.has('t-4')).toBe(false);
  });

  it('si Tenant no responde corta sin escribir', async () => {
    const store = new InMemoryZones();

    const outcome = await reconcileTenantTimeZonesOnce({ client: { listPage: async () => null }, store }, 200);

    expect(outcome).toEqual({ applied: 0, completed: false });
    expect(store.rows.size).toBe(0);
  });
});
