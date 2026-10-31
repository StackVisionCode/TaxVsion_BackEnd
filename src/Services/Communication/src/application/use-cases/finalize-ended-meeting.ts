import { lifecycleFrom, stopRecordingBySystem, type RecordingLifecycleOptionalDeps } from './recording-lifecycle.js';
import { randomUUID } from 'node:crypto';
import { logger } from '../../infrastructure/logger/logger.js';
import type { MeetingSnapshot } from '../../domain/meetings/meeting.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { SfuService } from '../ports/sfu-service.js';
import type { PresenceService } from '../ports/presence-service.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import { MeetingEventTypes, type MeetingEndedEvent, type MeetingQualitySummaryEvent } from '../../contracts/events/meeting-events.js';
import type { MeetingQualityAggregator } from '../services/meeting-quality-aggregator.js';
import type { MeetingReactionAggregator } from '../services/meeting-reaction-aggregator.js';
import {
  MeetingSocketEvents,
  type MeetingEndedDto,
  type MeetingStateDto,
} from '../../contracts/socket/meeting-socket-events.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import { stopMeetingRecording } from './stop-meeting-recording.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';
import { closeMeetingInvitations } from './close-meeting-invitations.js';
import { setConversationArchived, type SetConversationArchivedDeps } from './set-conversation-archived.js';
import { computeMeetingUniquenessKey } from '../../domain/conversations/uniqueness-key.js';

export interface FinalizeEndedMeetingDeps extends MeetingEventLogDeps, Omit<RecordingLifecycleOptionalDeps, 'meetings'> {
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly sfu: SfuService;
  readonly presence: PresenceService;
  readonly recordingSessions: RecordingSessionRepository;
  readonly idempotency: IdempotencyStore;
  /** Opcional: sin socket (job offline) se omiten los avisos realtime, no el resto del cierre. */
  readonly emitter?: RealtimeEmitter | undefined;
  /** F4: resumen de calidad al terminar (ADD-08). */
  readonly qualityAggregator?: MeetingQualityAggregator | undefined;
  /** F5 (D-50): minuto abierto de reacciones -> ultima fila `reaction.summary`. */
  readonly reactionAggregator?: MeetingReactionAggregator | undefined;
  /** F8a (D-01): el chat de la sala queda en solo lectura; el historial se ve en la ficha. */
  readonly conversations?: SetConversationArchivedDeps['conversations'] | undefined;
}

export interface FinalizeEndedMeetingInput {
  readonly tenantId: string;
  readonly correlationId: string;
  /** Snapshot ya Ended (el dominio aplicó end/endBySystem y se guardó). */
  readonly snapshot: MeetingSnapshot;
  readonly actorUserId: string | null;
}

/**
 * Cierre ordenado único (T-08/T-39) para cualquier fin de reunión: End del host, tope de duración,
 * sala vacía, sucesión sin staff, offboarding o backfill. Detiene la grabación si seguía activa,
 * libera el SFU y el "busy" de todos, avisa a la sala y a las listas con el motivo, publica
 * `meeting.ended.v1{reason}` y deja la fila en el MeetingEventLog.
 */
export async function finalizeEndedMeeting(input: FinalizeEndedMeetingInput, deps: FinalizeEndedMeetingDeps): Promise<void> {
  const { snapshot } = input;
  const reason = snapshot.endReason ?? 'Host';
  const endedAt = snapshot.endedAtUtc ?? new Date();

  // El ultimo minuto de reacciones entra al timeline antes de componer (F7).
  for (const bucket of deps.reactionAggregator?.flush(snapshot.id) ?? []) {
    await recordMeetingEvent(deps, {
      tenantId: input.tenantId,
      meetingId: snapshot.id,
      type: MeetingEventType.ReactionSummary,
      payload: { minuteUtc: bucket.minuteUtc, counts: bucket.counts },
      correlationId: input.correlationId,
    });
  }
  await stopRecordingIfActive(input, deps);

  await deps.sfu
    .closeMeeting(snapshot.id)
    .catch((err: unknown) => logger.warn({ err, meetingId: snapshot.id }, 'sfu closeMeeting failed'));
  await Promise.all(
    snapshot.participants.map((p) =>
      deps.presence
        .clearBusy({ tenantId: input.tenantId, userId: p.userId, sourceId: snapshot.id })
        .catch(() => undefined),
    ),
  );

  const event: MeetingEndedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.Ended,
    tenantId: input.tenantId,
    correlationId: input.correlationId,
    occurredOnUtc: endedAt.toISOString(),
    meetingId: snapshot.id,
    hostUserId: snapshot.hostUserId,
    endedAtUtc: endedAt.toISOString(),
    durationSeconds: snapshot.durationSeconds ?? 0,
    participantCount: snapshot.participants.length,
    recordingFileId: snapshot.recordingFileId,
    reason,
  };
  await deps.publisher.enqueue(event);
  const quality = deps.qualityAggregator?.summarize(snapshot.id) ?? null;
  if (quality) {
    const summary: MeetingQualitySummaryEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.QualitySummary,
      tenantId: input.tenantId,
      correlationId: input.correlationId,
      occurredOnUtc: endedAt.toISOString(),
      meetingId: snapshot.id,
      ...quality,
      endedAtUtc: endedAt.toISOString(),
    };
    await deps.publisher.enqueue(summary).catch(() => undefined);
  }
  // F8a (D-01): el chat de la sala queda en solo lectura (nadie escribe ni edita tras el End).
  if (deps.conversations) {
    await setConversationArchived(
      { tenantId: input.tenantId, archived: true, uniquenessKey: computeMeetingUniquenessKey(snapshot.id), now: endedAt },
      { conversations: deps.conversations, emitter: deps.emitter },
    );
  }
  // F6 (D-48): las invitaciones que seguian abiertas se cierran con la reunion.
  await closeMeetingInvitations(
    { tenantId: input.tenantId, meetingId: snapshot.id, reason: 'Ended', correlationId: input.correlationId, now: endedAt },
    deps,
  ).catch((err: unknown) => logger.warn({ err, meetingId: snapshot.id }, 'closeMeetingInvitations failed'));
  await recordMeetingEvent(deps, {
    tenantId: input.tenantId,
    meetingId: snapshot.id,
    type: MeetingEventType.Ended,
    actorUserId: input.actorUserId,
    payload: { reason, durationSeconds: snapshot.durationSeconds ?? 0 },
    correlationId: input.correlationId,
    at: endedAt,
  });

  if (!deps.emitter) return;
  const state: MeetingStateDto = {
    meetingId: snapshot.id,
    status: 'Ended',
    isLocked: snapshot.isLocked,
    hostUserId: snapshot.hostUserId,
    sequence: 0,
    endReason: reason,
  };
  const envelope = <T>(payload: T) => ({
    eventId: randomUUID(),
    correlationId: input.correlationId,
    emittedAtUtc: endedAt.toISOString(),
    payload,
  });
  deps.emitter.emitToMeeting({ tenantId: input.tenantId, meetingId: snapshot.id, event: MeetingSocketEvents.StateChanged, envelope: envelope(state) });
  // Lista de participantes e invitados: la fila pasa de "upcoming" a "past" con el motivo.
  const endedDto: MeetingEndedDto = { meetingId: snapshot.id, reason };
  const invitations = await deps.meetings.listInvitationsByMeeting(input.tenantId, snapshot.id).catch(() => []);
  const inviteeUserIds = invitations
    .map((inv) => inv.toSnapshot())
    .filter((s) => s.revokedAtUtc === null && s.inviteeUserId !== null)
    .map((s) => s.inviteeUserId!);
  for (const userId of new Set<string>([...snapshot.participants.map((p) => p.userId), ...inviteeUserIds])) {
    deps.emitter.emitToUser({ tenantId: input.tenantId, userId, event: MeetingSocketEvents.Ended, envelope: envelope(endedDto) });
  }
}

/** Una grabación en curso no sobrevive al fin de la reunión: se detiene como lo haría el host. */
async function stopRecordingIfActive(input: FinalizeEndedMeetingInput, deps: FinalizeEndedMeetingDeps): Promise<void> {
  const lifecycle = lifecycleFrom(deps);
  if (lifecycle) {
    // F7: corte de sistema (sin actor): el host pudo haberse ido y la reunion ya esta Ended.
    await stopRecordingBySystem(
      { tenantId: input.tenantId, scope: RecordingScope.Meeting, scopeId: input.snapshot.id, reason: 'MeetingEnded', correlationId: input.correlationId },
      lifecycle,
    ).catch((err: unknown) => logger.warn({ err, meetingId: input.snapshot.id }, 'stop recording on meeting end failed'));
    return;
  }
  const session = await deps.recordingSessions
    .findByScope(input.tenantId, RecordingScope.Meeting, input.snapshot.id)
    .catch(() => null);
  if (!session || session.toSnapshot().state !== 'Recording' || !deps.emitter) return;
  await stopMeetingRecording(
    {
      tenantId: input.tenantId,
      correlationId: input.correlationId,
      clientKey: `meeting-ended:${input.snapshot.id}`,
      meetingId: input.snapshot.id,
      actorUserId: input.snapshot.hostUserId,
    },
    { ...deps, emitter: deps.emitter },
  ).catch((err: unknown) => logger.warn({ err, meetingId: input.snapshot.id }, 'stop recording on meeting end failed'));
}
