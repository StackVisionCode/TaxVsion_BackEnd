import { logger } from '../logger/logger.js';
import type { CustomerAssignmentProjectionRepository } from '../../application/ports/customer-assignment-projection-repository.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';
import type { HttpCustomerAssignmentsReconciliationClient } from '../customer/http-customer-assignments-reconciliation-client.js';

/**
 * Auto-reparacion de la proyeccion M:N CustomerAssignmentProjection contra la
 * fuente autoritativa (P2.5): re-pagina TODOS los customers CON asignaciones de
 * TODOS los tenants via el endpoint M2M cross-tenant
 * `internal/customers/assignments/reconciliation` y REEMPLAZA el set local de
 * cada uno (version-guarded: no pisa un snapshot en vivo mas nuevo). Siembra el
 * gate restrictCustomerChatToAssignedPreparer al encenderlo — sin esto, un
 * tenant que lo active bloquearia chats con customers ya asignados hasta que
 * ocurriera un cambio de asignacion.
 *
 * Idempotente (mismo replace que el consumer del snapshot). Corre bajo
 * RedisDistributedLock (un pod por tick) + una vez al boot, mismo patron que
 * customer-reconciliation-scheduler (directorio). Fail-soft: si el endpoint no
 * responde, corta la pasada y reintenta en el siguiente tick.
 */
const LOCK_KEY = 'comm:lock:customer-assignment-reconciliation-scheduler';
const PAGE_SIZE = 200;

export interface CustomerAssignmentReconciliationSchedulerConfig {
  readonly enabled: boolean;
  readonly intervalHours: number;
}

export function startCustomerAssignmentReconciliationScheduler(
  config: CustomerAssignmentReconciliationSchedulerConfig,
  deps: {
    client: HttpCustomerAssignmentsReconciliationClient;
    customerAssignments: CustomerAssignmentProjectionRepository;
    lock: RedisDistributedLock;
  },
): { stop(): void } {
  if (!config.enabled) {
    logger.info('CustomerAssignmentReconciliationScheduler disabled by config; not starting');
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
        logger.warn({ page }, 'CustomerAssignmentReconciliationScheduler: aborted (Customer.Api unreachable)');
        return;
      }

      for (const row of result.items) {
        if (!row.version) {
          skipped += 1;
          continue;
        }
        // Version-guarded: no pisar un snapshot en vivo mas nuevo que ya llego por evento.
        const applied = await deps.customerAssignments.getVersion(row.tenantId, row.customerId);
        if (applied && row.version <= applied) {
          skipped += 1;
          continue;
        }
        await deps.customerAssignments.replace(row.tenantId, row.customerId, row.assigneeUserIds, row.version);
        upserted += 1;
      }

      if (!result.hasMore) break;
      page += 1;
    }

    logger.info({ upserted, skipped }, 'CustomerAssignmentReconciliationScheduler: tick complete');
  };

  const tick = async (): Promise<void> => {
    try {
      const ran = await deps.lock.withLock(LOCK_KEY, Math.max(intervalMs * 2, 60_000), reconcile);
      if (ran === undefined) {
        logger.debug('CustomerAssignmentReconciliationScheduler: lock held by another pod, skipping tick');
      }
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'CustomerAssignmentReconciliationScheduler tick failed');
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
