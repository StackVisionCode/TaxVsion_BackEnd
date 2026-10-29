import { logger } from '../logger/logger.js';
import type { UserDirectoryRepository } from '../../application/ports/user-directory-repository.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';
import type { HttpUserDirectoryReconciliationClient } from '../auth/http-user-directory-reconciliation-client.js';

/**
 * Auto-reparacion de UserDirectoryEntry contra Auth (F5 de avatares). Antes no tenia ninguna: si se
 * perdia un `auth.user.profile_updated.v1` (o llegaba antes que `registered`), el nombre y la foto
 * quedaban viejos para siempre. Re-pagina TODOS los usuarios de TODOS los tenants via el M2M
 * `internal/users/directory/reconciliation` y hace upsert de cada fila con nombre, email, actorType,
 * estado y foto.
 *
 * Mismo patron que customer-reconciliation-scheduler: RedisDistributedLock (un pod por tick) + una
 * pasada al boot + intervalo. Idempotente. Fail-soft: si Auth no responde, corta y reintenta en el
 * siguiente tick. No emite realtime: es reparacion silenciosa; el front ve el cambio en la proxima lectura.
 */
const LOCK_KEY = 'comm:lock:user-directory-reconciliation-scheduler';
const PAGE_SIZE = 200;
// Topes de las columnas de UserDirectoryEntry.
const DISPLAY_NAME_MAX = 200;
const EMAIL_MAX = 320;

export interface UserDirectoryReconciliationSchedulerConfig {
  readonly enabled: boolean;
  readonly intervalHours: number;
}

export interface UserDirectoryReconciliationDeps {
  readonly client: Pick<HttpUserDirectoryReconciliationClient, 'listPage'>;
  readonly userDirectory: Pick<UserDirectoryRepository, 'upsert'>;
}

export interface UserDirectoryReconciliationOutcome {
  readonly upserted: number;
  readonly skipped: number;
  /** false si Auth no respondio a mitad de la pasada. */
  readonly completed: boolean;
}

/** Una pasada completa (todas las paginas). Exportada para tests. */
export async function reconcileUserDirectoryOnce(
  deps: UserDirectoryReconciliationDeps,
): Promise<UserDirectoryReconciliationOutcome> {
  let page = 1;
  let upserted = 0;
  let skipped = 0;

  for (;;) {
    const result = await deps.client.listPage(page, PAGE_SIZE);
    if (result === null) {
      logger.warn({ page }, 'UserDirectoryReconciliationScheduler: aborted (Auth.Api unreachable)');
      return { upserted, skipped, completed: false };
    }

    for (const row of result.items) {
      // Sin id, tenant o email no hay fila valida (Email es NOT NULL en la tabla).
      if (!row.userId || !row.tenantId || !row.email) {
        skipped += 1;
        continue;
      }
      const displayName = (row.displayName.trim() || row.email).slice(0, DISPLAY_NAME_MAX);
      await deps.userDirectory.upsert({
        userId: row.userId,
        tenantId: row.tenantId,
        displayName,
        email: row.email.slice(0, EMAIL_MAX),
        isActive: row.isActive,
        // Auth es la fuente de verdad: null = sin foto (la limpia).
        avatarFileId: row.avatarFileId,
        ...(row.actorType ? { actorType: row.actorType } : {}),
      });
      upserted += 1;
    }

    if (!result.hasMore) break;
    page += 1;
  }

  return { upserted, skipped, completed: true };
}

export function startUserDirectoryReconciliationScheduler(
  config: UserDirectoryReconciliationSchedulerConfig,
  deps: UserDirectoryReconciliationDeps & { lock: Pick<RedisDistributedLock, 'withLock'> },
): { stop(): void; tick(): Promise<void> } {
  if (!config.enabled) {
    logger.info('UserDirectoryReconciliationScheduler disabled by config; not starting');
    return { stop() {}, tick: async () => {} };
  }

  const intervalMs = config.intervalHours * 3_600_000;

  const tick = async (): Promise<void> => {
    try {
      const outcome = await deps.lock.withLock(LOCK_KEY, Math.max(intervalMs * 2, 60_000), () =>
        reconcileUserDirectoryOnce(deps),
      );
      if (outcome === undefined) {
        logger.debug('UserDirectoryReconciliationScheduler: lock held by another pod, skipping tick');
      } else if (outcome.completed) {
        logger.info(
          { upserted: outcome.upserted, skipped: outcome.skipped },
          'UserDirectoryReconciliationScheduler: tick complete',
        );
      }
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'UserDirectoryReconciliationScheduler tick failed');
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
