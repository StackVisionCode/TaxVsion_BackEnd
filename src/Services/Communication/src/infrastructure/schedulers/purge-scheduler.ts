import type { PrismaClient } from '@prisma/client';
import { logger } from '../logger/logger.js';
import type { SettingsRepository } from '../../application/ports/settings-repository.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';

/**
 * Purga datos viejos, dos categorias distintas:
 *   1. Datos de negocio (Message + MessageReceipt via cascade): SOLO para
 *      tenants con `PurgeEnabled=true` en TenantCommunicationSettings,
 *      usando su `MessageRetentionDays` configurado. Feature-flag OFF por
 *      default — un tenant admin lo prende explicitamente.
 *   2. Datos operativos (OutboxMessage ya publicado, ProcessedEvent inbox,
 *      IdempotencyRecord expirado): retencion fija global, no depende de
 *      config de tenant — son solo housekeeping interno, no visibles al
 *      usuario final.
 *
 * Corre bajo `RedisDistributedLock` (un solo pod ejecuta por tick) — mismo
 * patron que outbox-drainer y missed-call-scheduler.
 *
 * NO cubierto todavia (documentado, no silencioso): Call/CallParticipant,
 * Meeting/MeetingParticipant/MeetingInvitation, NotificationEntry,
 * CommunicationAnalyticsSnapshot, AttachmentTracking — ninguno tiene todavia
 * un campo de retencion configurado en el schema.
 */
const OUTBOX_RETENTION_DAYS = 7;
const PROCESSED_EVENT_RETENTION_DAYS = 14;
const IDEMPOTENCY_RETENTION_DAYS = 30;
const LOCK_KEY = 'comm:lock:purge-scheduler';

export interface PurgeSchedulerConfig {
  readonly intervalHours: number;
}

export function startPurgeScheduler(
  config: PurgeSchedulerConfig,
  deps: { prisma: PrismaClient; settings: SettingsRepository; lock: RedisDistributedLock },
): { stop(): void } {
  const intervalMs = config.intervalHours * 3_600_000;

  const purge = async (): Promise<void> => {
    const now = new Date();

    const purgeEnabledTenants = await deps.settings.listPurgeEnabled();
    let purgedMessages = 0;
    for (const tenant of purgeEnabledTenants) {
      const cutoff = new Date(now.getTime() - tenant.messageRetentionDays * 86_400_000);
      const result = await deps.prisma.message.deleteMany({
        where: { TenantId: tenant.tenantId, CreatedAtUtc: { lt: cutoff } },
      });
      purgedMessages += result.count;
    }

    const outboxCutoff = new Date(now.getTime() - OUTBOX_RETENTION_DAYS * 86_400_000);
    const outboxResult = await deps.prisma.outboxMessage.deleteMany({
      where: { PublishedAtUtc: { not: null, lt: outboxCutoff } },
    });

    const inboxCutoff = new Date(now.getTime() - PROCESSED_EVENT_RETENTION_DAYS * 86_400_000);
    const inboxResult = await deps.prisma.processedEvent.deleteMany({
      where: { ProcessedAtUtc: { lt: inboxCutoff } },
    });

    const idempotencyCutoff = new Date(now.getTime() - IDEMPOTENCY_RETENTION_DAYS * 86_400_000);
    const idempotencyResult = await deps.prisma.idempotencyRecord.deleteMany({
      where: { CreatedAtUtc: { lt: idempotencyCutoff } },
    });

    logger.info(
      {
        tenantsWithPurgeEnabled: purgeEnabledTenants.length,
        purgedMessages,
        purgedOutboxMessages: outboxResult.count,
        purgedProcessedEvents: inboxResult.count,
        purgedIdempotencyRecords: idempotencyResult.count,
      },
      'PurgeScheduler: tick complete',
    );
  };

  const tick = async (): Promise<void> => {
    try {
      const ran = await deps.lock.withLock(LOCK_KEY, Math.max(intervalMs * 2, 60_000), purge);
      if (ran === undefined) {
        logger.debug('PurgeScheduler: lock held by another pod, skipping tick');
      }
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'PurgeScheduler tick failed');
    }
  };

  // Corre una vez al boot ademas del interval — a diferencia de los otros
  // schedulers, esperar `intervalHours` (tipicamente 24h) tras un deploy
  // fresco antes de la primera purga dejaria crecer las tablas operativas
  // sin necesidad.
  void tick();
  const handle = setInterval(() => void tick(), intervalMs);
  return {
    stop() {
      clearInterval(handle);
    },
  };
}
