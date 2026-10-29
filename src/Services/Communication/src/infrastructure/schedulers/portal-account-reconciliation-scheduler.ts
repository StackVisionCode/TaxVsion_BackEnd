import { logger } from '../logger/logger.js';
import type { CustomerPortalAccountReconcileWriter } from '../../application/ports/customer-portal-account-repository.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';
import type { HttpPortalAccountsReconciliationClient } from '../auth/http-portal-accounts-reconciliation-client.js';

/**
 * Auto-reparacion de CustomerPortalAccount (userId de portal ↔ customerId) contra Auth. La tabla solo se
 * alimentaba con eventos (`registered`/`deactivated`/`reactivated`/`offboarded`): si se perdia uno, el
 * cliente quedaba "no chateable" o con la foto equivocada en el chat para siempre.
 *
 * Por fila de Auth (ver CustomerPortalAccountReconcileWriter): activa → upsert; inactiva → baja solo
 * la fila de ese mismo usuario. NO se borran filas que Auth no devuelve: el endpoint lista todas las
 * cuentas de portal en cualquier estado (Auth no borra usuarios, los desactiva/retira), asi que una
 * ausencia no prueba nada — una pagina corrida o un fallo a mitad de pasada borraria cuentas validas.
 * Tampoco hay regla de borrado en el codigo existente (los consumers solo marcan IsActive).
 *
 * Mismo patron que user-directory-reconciliation-scheduler: Redis lock, boot + intervalo, fail-soft.
 */
const LOCK_KEY = 'comm:lock:portal-account-reconciliation-scheduler';
const PAGE_SIZE = 200;

export interface PortalAccountReconciliationSchedulerConfig {
  readonly enabled: boolean;
  readonly intervalHours: number;
}

export interface PortalAccountReconciliationDeps {
  readonly client: Pick<HttpPortalAccountsReconciliationClient, 'listPage'>;
  readonly portalAccounts: CustomerPortalAccountReconcileWriter;
}

export interface PortalAccountReconciliationOutcome {
  readonly upserted: number;
  readonly deactivated: number;
  readonly unchanged: number;
  readonly skipped: number;
  /** false si Auth no respondio a mitad de la pasada. */
  readonly completed: boolean;
}

/** Una pasada completa (todas las paginas). Exportada para tests. */
export async function reconcilePortalAccountsOnce(
  deps: PortalAccountReconciliationDeps,
): Promise<PortalAccountReconciliationOutcome> {
  const counts = { upserted: 0, deactivated: 0, unchanged: 0, skipped: 0 };
  let page = 1;

  for (;;) {
    const result = await deps.client.listPage(page, PAGE_SIZE);
    if (result === null) {
      logger.warn({ page }, 'PortalAccountReconciliationScheduler: aborted (Auth.Api unreachable)');
      return { ...counts, completed: false };
    }

    for (const row of result.items) {
      if (!row.customerId || !row.userId || !row.tenantId) {
        counts.skipped += 1;
        continue;
      }
      const outcome = await deps.portalAccounts.applyReconciled(row);
      counts[outcome] += 1;
    }

    if (!result.hasMore) break;
    page += 1;
  }

  return { ...counts, completed: true };
}

export function startPortalAccountReconciliationScheduler(
  config: PortalAccountReconciliationSchedulerConfig,
  deps: PortalAccountReconciliationDeps & { lock: Pick<RedisDistributedLock, 'withLock'> },
): { stop(): void; tick(): Promise<void> } {
  if (!config.enabled) {
    logger.info('PortalAccountReconciliationScheduler disabled by config; not starting');
    return { stop() {}, tick: async () => {} };
  }

  const intervalMs = config.intervalHours * 3_600_000;

  const tick = async (): Promise<void> => {
    try {
      const outcome = await deps.lock.withLock(LOCK_KEY, Math.max(intervalMs * 2, 60_000), () =>
        reconcilePortalAccountsOnce(deps),
      );
      if (outcome === undefined) {
        logger.debug('PortalAccountReconciliationScheduler: lock held by another pod, skipping tick');
      } else if (outcome.completed) {
        logger.info(
          {
            upserted: outcome.upserted,
            deactivated: outcome.deactivated,
            unchanged: outcome.unchanged,
            skipped: outcome.skipped,
          },
          'PortalAccountReconciliationScheduler: tick complete',
        );
      }
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'PortalAccountReconciliationScheduler tick failed');
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
