import { randomUUID } from 'node:crypto';
import { logger } from '../../infrastructure/logger/logger.js';
import { MeetingEventScope, type MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingEventLogRepository } from '../ports/meeting-event-log-repository.js';

export interface MeetingEventLogDeps {
  readonly meetingEvents?: MeetingEventLogRepository | undefined;
}

export interface RecordMeetingEventInput {
  readonly tenantId: string;
  readonly meetingId: string;
  /** F7: las llamadas usan el mismo log con scope Call (meetingId = id de la llamada). */
  readonly scope?: MeetingEventScope;
  readonly type: MeetingEventType;
  readonly actorUserId?: string | null;
  readonly targetUserId?: string | null;
  readonly payload?: Readonly<Record<string, unknown>> | null;
  readonly correlationId?: string | null;
  readonly at?: Date;
}

/**
 * Escritor único del MeetingEventLog (C.3). Best-effort a propósito: un fallo al registrar nunca
 * deshace la acción que ya ocurrió (igual que el antiguo log de host); se avisa y se sigue.
 */
export async function recordMeetingEvent(deps: MeetingEventLogDeps, input: RecordMeetingEventInput): Promise<void> {
  if (!deps.meetingEvents) return;
  try {
    await deps.meetingEvents.append({
      id: randomUUID(),
      tenantId: input.tenantId,
      scope: input.scope ?? MeetingEventScope.Meeting,
      scopeId: input.meetingId,
      at: input.at ?? new Date(),
      type: input.type,
      actorUserId: input.actorUserId ?? null,
      targetUserId: input.targetUserId ?? null,
      payload: input.payload ?? null,
      correlationId: input.correlationId ?? null,
    });
  } catch (err) {
    logger.warn({ err, meetingId: input.meetingId, type: input.type }, 'MeetingEventLog append failed');
  }
}
