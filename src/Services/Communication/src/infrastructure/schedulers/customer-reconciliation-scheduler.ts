import { logger } from '../logger/logger.js';
import type { CustomerDirectoryRepository } from '../../application/ports/customer-directory-repository.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';
import type { HttpCustomerReconciliationClient } from '../customer/http-customer-reconciliation-client.js';

/**
 * Auto-reparacion de la proyeccion CustomerDirectoryEntry contra la fuente
 * autoritativa: re-pagina TODOS los customers de TODOS los tenants via el
 * endpoint M2M cross-tenant `customers/internal/reconciliation` y hace upsert de
 * cada fila. Cierra la deuda de raiz — la proyeccion solo se poblaba con eventos
 * en vivo, asi que cuando se perdia un evento (o llegaba una rafaga que
 * outrunneaba al consumer) quedaba corta sin forma de recuperarse.
 *
 * Idempotente: usa el MISMO customerDirectory.upsert que los consumers de
 * customer.created/updated, asi que converge sin duplicar. Corre bajo
 * RedisDistributedLock (un pod por tick) + una vez al boot, mismo patron que
 * purge-scheduler. Fail-soft: si el endpoint no responde, corta la pasada y
 * reintenta en el siguiente tick.
 */
const LOCK_KEY = 'comm:lock:customer-reconciliation-scheduler';
const PAGE_SIZE = 200;

export interface CustomerReconciliationSchedulerConfig {
  readonly enabled: boolean;
  readonly intervalHours: number;
}

export function startCustomerReconciliationScheduler(
  config: CustomerReconciliationSchedulerConfig,
  deps: {
    client: HttpCustomerReconciliationClient;
    customerDirectory: CustomerDirectoryRepository;
    lock: RedisDistributedLock;
  },
): { stop(): void } {
  if (!config.enabled) {
    logger.info('CustomerReconciliationScheduler disabled by config; not starting');
    return { stop() {} };
  }

  const intervalMs = config.intervalHours * 3_600_000;

  const reconcile = async (): Promise<void> => {
    let page = 1;
    let upserted = 0;
    let skipped = 0;

    for (;;) {
      const result = await deps.client.listPage(page, PAGE_SIZE);
      if (result === null) {
        logger.warn({ page }, 'CustomerReconciliationScheduler: aborted (Customer.Api unreachable)');
        return;
      }

      for (const row of result.items) {
        // Mismo criterio que los consumers customer.created/updated: sin id/nombre/email no se
        // proyecta (el autocomplete de invitaciones necesita ambos).
        if (!row.customerId || !row.displayName || !row.primaryEmail) {
          skipped += 1;
          continue;
        }
        await deps.customerDirectory.upsert({
          customerId: row.customerId,
          tenantId: row.tenantId,
          displayName: row.displayName,
          email: row.primaryEmail,
          isActive: row.status.toLowerCase() === 'active',
        });
        upserted += 1;
      }

      if (!result.hasMore) break;
      page += 1;
    }

    logger.info({ upserted, skipped }, 'CustomerReconciliationScheduler: tick complete');
  };

  const tick = async (): Promise<void> => {
    try {
      const ran = await deps.lock.withLock(LOCK_KEY, Math.max(intervalMs * 2, 60_000), reconcile);
      if (ran === undefined) {
        logger.debug('CustomerReconciliationScheduler: lock held by another pod, skipping tick');
      }
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'CustomerReconciliationScheduler tick failed');
    }
  };

  void tick();
  const handle = setInterval(() => void tick(), intervalMs);
  return {
    stop() {
      clearInterval(handle);
    },
  };
}
