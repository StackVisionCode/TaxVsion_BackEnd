import { logger } from '../../infrastructure/logger/logger.js';

/**
 * Audit estructurado de acciones de Host/Cohost sobre un meeting (Fase Backend 6).
 * Escribe un log Pino con `event_type: "meeting.host.action"` — deliberadamente NO
 * es una tabla en Prisma: pino ya envia todo a Loki via OTLP (ver logger.ts), y una
 * tabla propia obligaria a otra migracion + pruning aparte. Si en el futuro se
 * necesita auditoria consultable via API/UI (no solo Loki), promover a tabla y
 * migrar los logs — hasta entonces esto cumple con "actor, action, target,
 * meetingId, tenantId, occurredAt" que pide el spec.
 */
export interface HostAuditEntry {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly action:
    | 'meeting.admit'
    | 'meeting.remove'
    | 'meeting.deny_waiting_room'
    | 'meeting.lock'
    | 'meeting.unlock'
    | 'meeting.mute_all'
    | 'meeting.transfer_host'
    | 'meeting.promote_cohost'
    | 'meeting.demote_cohost'
    | 'meeting.cancel'
    | 'meeting.reschedule';
  readonly actorUserId: string;
  readonly targetUserId?: string;
  readonly correlationId?: string;
  readonly metadata?: Record<string, unknown>;
}

export function logHostAction(entry: HostAuditEntry): void {
  logger.info(
    {
      event_type: 'meeting.host.action',
      tenantId: entry.tenantId,
      meetingId: entry.meetingId,
      action: entry.action,
      actorUserId: entry.actorUserId,
      ...(entry.targetUserId !== undefined ? { targetUserId: entry.targetUserId } : {}),
      ...(entry.correlationId !== undefined ? { correlationId: entry.correlationId } : {}),
      ...(entry.metadata !== undefined ? { metadata: entry.metadata } : {}),
      occurredAtUtc: new Date().toISOString(),
    },
    `Host action: ${entry.action}`,
  );
}
