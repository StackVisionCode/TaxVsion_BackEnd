import { logger } from '../logger/logger.js';
import type { TenantTimeZoneStore } from '../../application/ports/tenant-time-zone-store.js';
import { normalizeTimeZoneId, reconcileWithSource } from '../../domain/time-zones/tenant-time-zone.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';

/**
 * Siembra y repara `TenantTimeZoneProjection` contra el directorio de Tenant: cubre oficinas creadas antes de esta
 * proyección y eventos perdidos. Mismo patrón que user-directory-reconciliation-scheduler: lock de Redis (un pod
 * por tick), una pasada al arrancar y luego por intervalo. Idempotente; si Tenant no responde, corta.
 */
const LOCK_KEY = 'comm:lock:tenant-time-zone-reconciliation-scheduler';
const PAGE_SIZE = 200;

export interface TenantDirectoryZoneRow {
  readonly tenantId: string;
  readonly timeZoneId: string;
}

export interface TenantTimeZoneReconciliationDeps {
  readonly client: { listPage(page: number, size: number): Promise<TenantDirectoryZoneRow[] | null> };
  readonly store: TenantTimeZoneStore;
}

export interface TenantTimeZoneReconciliationOutcome {
  readonly applied: number;
  /** false si Tenant no respondió a mitad de la pasada. */
  readonly completed: boolean;
}

/** Una pasada completa (todas las páginas). Exportada para tests. */
export async function reconcileTenantTimeZonesOnce(
  deps: TenantTimeZoneReconciliationDeps,
  pageSize: number = PAGE_SIZE,
): Promise<TenantTimeZoneReconciliationOutcome> {
  // Foto actual de la fuente: más nueva que cualquier evento ya aplicado sin versión.
  const readAtUtc = new Date();
  let applied = 0;
  for (let page = 1; ; page++) {
    const rows = await deps.client.listPage(page, pageSize);
    if (rows === null) {
      logger.warn({ page }, 'TenantTimeZoneReconciliationScheduler: aborted (Tenant.Api unreachable)');
      return { applied, completed: false };
    }
    for (const row of rows) {
      const zone = normalizeTimeZoneId(row.timeZoneId);
      if (!row.tenantId || !zone) continue;
      const next = reconcileWithSource(await deps.store.find(row.tenantId), row.tenantId, zone, readAtUtc);
      if (!next) continue;
      await deps.store.save(next);
      applied += 1;
    }
    if (rows.length < pageSize) break;
  }
  return { applied, completed: true };
}

export function startTenantTimeZoneReconciliationScheduler(
  config: { readonly enabled: boolean; readonly intervalHours: number },
  deps: TenantTimeZoneReconciliationDeps & { lock: Pick<RedisDistributedLock, 'withLock'> },
): { stop(): void; tick(): Promise<void> } {
  if (!config.enabled) {
    logger.info('TenantTimeZoneReconciliationScheduler disabled by config; not starting');
    return { stop() {}, tick: async () => {} };
  }

  const intervalMs = config.intervalHours * 3_600_000;
  const tick = async (): Promise<void> => {
    try {
      const outcome = await deps.lock.withLock(LOCK_KEY, Math.max(intervalMs * 2, 60_000), () =>
        reconcileTenantTimeZonesOnce(deps),
      );
      if (outcome === undefined) {
        logger.debug('TenantTimeZoneReconciliationScheduler: lock held by another pod, skipping tick');
      } else if (outcome.completed) {
        logger.info({ applied: outcome.applied }, 'TenantTimeZoneReconciliationScheduler: tick complete');
      }
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'TenantTimeZoneReconciliationScheduler tick failed');
    }
  };

  void tick();
  const handle = setInterval(() => void tick(), intervalMs);
  return {
    stop() {
      clearInterval(handle);
    },
    tick,
  };
}
