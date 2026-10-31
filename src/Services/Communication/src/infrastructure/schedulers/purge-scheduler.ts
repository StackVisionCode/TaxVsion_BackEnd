import type { PrismaClient } from '@prisma/client';
import { logger } from '../logger/logger.js';
import type { SettingsRepository } from '../../application/ports/settings-repository.js';
import type { EffectiveSettingsResolver } from '../../application/ports/effective-settings-resolver.js';
import type { RedisDistributedLock } from '../redis/redis-distributed-lock.js';
import type { AnalyticsRepository } from '../../application/ports/analytics-repository.js';
import { dayInTimeZone } from '../../application/services/analytics-recorder.js';
import { purgeCommunicationHistory } from './history-purge.js';

/**
 * Purga datos viejos, dos categorias distintas:
 *   1. Datos de negocio (Message + MessageReceipt via cascade): SOLO para
 *      tenants con `PurgeEnabled=true` en TenantCommunicationSettings,
 *      usando su `MessageRetentionDays` configurado. Feature-flag OFF por
 *      default — un tenant admin lo prende explicitamente.
 *   2. Datos operativos (OutboxMessage ya publicado, ProcessedEvent inbox,
 *      IdempotencyRecord expirado): retencion global de PlatformCommunicationSettings
 *      (leida en cada tick), no depende de config de tenant — son solo
 *      housekeeping interno, no visibles al usuario final.
 *
 * Corre bajo `RedisDistributedLock` (un solo pod ejecuta por tick) — mismo
 * patron que outbox-drainer y missed-call-scheduler.
 *
 *   3. F10 (ADD-08 T-73): snapshot de analitica (`analyticsRetentionDays`) e historico de
 *      calls/meetings/invitaciones (`callsRetentionDays`/`meetingsRetentionDays`/
 *      `invitationsRetentionDays`, null = no se purga), ver history-purge.ts.
 *
 * NO cubierto todavia (documentado, no silencioso): AttachmentTracking.
 */
const LOCK_KEY = 'comm:lock:purge-scheduler';

export interface PurgeSchedulerConfig {
  readonly intervalHours: number;
  readonly notificationReadRetentionDays: number;
  readonly notificationUnreadRetentionDays: number;
  readonly batchSize: number;
}

export function startPurgeScheduler(
  config: PurgeSchedulerConfig,
  deps: {
    prisma: PrismaClient;
    settings: SettingsRepository;
    lock: RedisDistributedLock;
    effectiveSettings: Pick<EffectiveSettingsResolver, 'platform'>;
    analytics?: Pick<AnalyticsRepository, 'purgeBefore'>;
  },
): { stop(): void } {
  const intervalMs = config.intervalHours * 3_600_000;

  const purge = async (): Promise<void> => {
    const now = new Date();
    const platform = await deps.effectiveSettings.platform();

    const purgeEnabledTenants = await deps.settings.listPurgeEnabled();
    let purgedMessages = 0;
    for (const tenant of purgeEnabledTenants) {
      const cutoff = new Date(now.getTime() - tenant.messageRetentionDays * 86_400_000);
      const result = await deps.prisma.message.deleteMany({
        where: { TenantId: tenant.tenantId, CreatedAtUtc: { lt: cutoff } },
      });
      purgedMessages += result.count;
    }

    const outboxCutoff = new Date(now.getTime() - platform.outboxRetentionDays * 86_400_000);
    const outboxResult = await deps.prisma.outboxMessage.deleteMany({
      where: { PublishedAtUtc: { not: null, lt: outboxCutoff } },
    });

    const inboxCutoff = new Date(now.getTime() - platform.processedEventRetentionDays * 86_400_000);
    const inboxResult = await deps.prisma.processedEvent.deleteMany({
      where: { ProcessedAtUtc: { lt: inboxCutoff } },
    });

    const idempotencyCutoff = new Date(now.getTime() - platform.idempotencyRetentionDays * 86_400_000);
    const idempotencyResult = await deps.prisma.idempotencyRecord.deleteMany({
      where: { CreatedAtUtc: { lt: idempotencyCutoff } },
    });
    // F11 (E-259): subidas de adjuntos reservadas que nunca se enviaron (mismo plazo que la idempotencia).
    const reservationsResult = await deps.prisma.attachmentUploadReservation.deleteMany({
      where: { CreatedAtUtc: { lt: idempotencyCutoff } },
    });

    const purgedNotifications = await purgeNotificationEntries(deps.prisma, {
      nowUtc: now,
      readRetentionDays: config.notificationReadRetentionDays,
      unreadRetentionDays: config.notificationUnreadRetentionDays,
      batchSize: config.batchSize,
    });

    // F10: dia de corte en la zona de analitica (los dias del snapshot estan cortados en esa zona).
    const analyticsCutoffDay = dayInTimeZone(
      new Date(now.getTime() - platform.analyticsRetentionDays * 86_400_000).toISOString(),
      platform.analyticsTimeZone,
    );
    const purgedAnalyticsDays = deps.analytics ? await deps.analytics.purgeBefore(analyticsCutoffDay) : 0;
    const history = await purgeCommunicationHistory(deps.prisma, {
      now,
      retention: {
        callsRetentionDays: platform.callsRetentionDays,
        meetingsRetentionDays: platform.meetingsRetentionDays,
        invitationsRetentionDays: platform.invitationsRetentionDays,
      },
      batchSize: config.batchSize,
    });

    logger.info(
      {
        tenantsWithPurgeEnabled: purgeEnabledTenants.length,
        purgedAnalyticsDays,
        purgedCalls: history.calls,
        purgedMeetings: history.meetings,
        purgedInvitations: history.invitations,
        purgedMessages,
        purgedOutboxMessages: outboxResult.count,
        purgedProcessedEvents: inboxResult.count,
        purgedIdempotencyRecords: idempotencyResult.count,
        purgedAttachmentReservations: reservationsResult.count,
        purgedNotifications,
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

export async function purgeNotificationEntries(
  prisma: PrismaClient,
  input: {
    nowUtc: Date;
    readRetentionDays: number;
    unreadRetentionDays: number;
    batchSize: number;
  },
): Promise<number> {
  const readCutoff = new Date(input.nowUtc.getTime() - input.readRetentionDays * 86_400_000);
  const unreadCutoff = new Date(input.nowUtc.getTime() - input.unreadRetentionDays * 86_400_000);
  let total = 0;

  while (true) {
    const rows = await prisma.notificationEntry.findMany({
      where: {
        OR: [
          { ReadAtUtc: { not: null, lt: readCutoff } },
          { ReadAtUtc: null, CreatedAtUtc: { lt: unreadCutoff } },
        ],
      },
      select: { Id: true },
      orderBy: { CreatedAtUtc: 'asc' },
      take: input.batchSize,
    });
    if (rows.length === 0) return total;

    const deleted = await prisma.notificationEntry.deleteMany({
      where: { Id: { in: rows.map((row) => row.Id) } },
    });
    total += deleted.count;
    if (rows.length < input.batchSize || deleted.count === 0) return total;
  }
}
