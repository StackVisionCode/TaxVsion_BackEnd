import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import {
  RecordingConsentPolicy,
  evaluateRecordingConsentPolicy,
  type RecordingConsentTerminalResponse,
} from '../../domain/recording/recording-consent.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RecordingSessionRepository, RecordingConsentRepository } from '../ports/recording-repository.js';
import type { SettingsRepository } from '../ports/settings-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { MeetingEventTypes, type MeetingRecordingStartedEvent } from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingRecordingStateChangedDto,
} from '../../contracts/socket/meeting-socket-events.js';
import type { RecordingConsentSnapshotEntry } from '../../contracts/events/integration-event.js';

/**
 * NO se dispara directamente desde un evento de socket — solo desde
 * respond-meeting-recording-consent.ts (auto-invoke tras cada respuesta) o
 * desde recording-consent-timeout-scheduler.ts (a los 30s). Por eso hace su
 * propia publicacion + emision (mismo criterio que transcript-consumers.ts:
 * modulos sin un "socket handler" sincronico que emita por ellos).
 *
 * Evalua la policy en el momento (no confia en un boolean pre-calculado por
 * el caller) leyendo directo de RecordingConsentRepository — asi respond y el
 * scheduler quedan sincronizados con una unica fuente de verdad.
 */
export interface StartMeetingRecordingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  /** Siempre RecordingSession.requestedByUserId — ya validado Host/Cohost al pedir la grabacion. */
  readonly actorUserId: string;
}

export interface StartMeetingRecordingDeps {
  readonly meetings: MeetingRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly recordingConsents: RecordingConsentRepository;
  readonly tenantSettings: SettingsRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

export async function startMeetingRecording(
  command: StartMeetingRecordingCommand,
  deps: StartMeetingRecordingDeps,
): Promise<Result<{ startedAtUtc: string }>> {
  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const session = await deps.recordingSessions.findByScope(command.tenantId, RecordingScope.Meeting, command.meetingId);
  if (!session) return Result.fail(makeError('RecordingSession.NotFound', 'No recording session for this meeting.'));
  if (session.sessionState !== 'Requesting') {
    return Result.fail(
      makeError('RecordingSession.NotRequesting', `Cannot start from ${session.sessionState}.`),
    );
  }

  const settings = await deps.tenantSettings.findByTenantId(command.tenantId);
  const policy = settings?.toSnapshot().recordingConsentPolicy ?? RecordingConsentPolicy.NoRejections;

  const allEntries = await deps.recordingConsents.listByScope(command.tenantId, RecordingScope.Meeting, command.meetingId);
  // Append-only puede tener varias filas por usuario (cambio de opinion) — nos
  // quedamos con la mas reciente por userId (listByScope ya ordena asc por
  // RecordedAtUtc, asi que la ultima iteracion gana), ignorando filas 'Requested'.
  const latestByUser = new Map<string, RecordingConsentSnapshotEntry>();
  for (const entry of allEntries) {
    const snap = entry.toSnapshot();
    if (snap.response === 'Requested') continue;
    latestByUser.set(snap.userId, {
      userId: snap.userId,
      response: snap.response,
      respondedAtUtc: snap.respondedAtUtc.toISOString(),
    });
  }
  const consentSnapshot: readonly RecordingConsentSnapshotEntry[] = [...latestByUser.values()];
  const consentEntries: readonly RecordingConsentTerminalResponse[] = consentSnapshot.map((e) => ({
    userId: e.userId,
    response: e.response,
  }));

  const participantUserIds = meeting.getJoinedParticipants().map((p) => p.userId);
  const policyAllows = evaluateRecordingConsentPolicy({
    policy,
    participantUserIds,
    requestedByUserId: session.requestedByUserId,
    consentEntries,
  });
  if (!policyAllows) {
    return Result.fail(makeError('RecordingSession.ConsentPolicyNotSatisfied', 'Consent policy does not allow starting yet.'));
  }

  const startResult = meeting.startRecording({ actorUserId: command.actorUserId, session, policyAllows: true });
  if (!startResult.isSuccess) return Result.fail(startResult.error);
  await deps.recordingSessions.save(startResult.value);

  const snap = startResult.value.toSnapshot();
  const startedAtUtc = (snap.startedAtUtc ?? new Date()).toISOString();

  const event: MeetingRecordingStartedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.RecordingStarted,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: startedAtUtc,
    meetingId: command.meetingId,
    startedByUserId: command.actorUserId,
    startedAtUtc,
    consentSnapshot,
  };
  await deps.publisher.enqueue(event);

  const dto: MeetingRecordingStateChangedDto = { meetingId: command.meetingId, state: 'Recording', updatedAtUtc: startedAtUtc };
  deps.emitter.emitToMeeting({
    tenantId: command.tenantId,
    meetingId: command.meetingId,
    event: MeetingSocketEvents.RecordingStateChanged,
    envelope: {
      eventId: randomUUID(),
      correlationId: command.correlationId,
      emittedAtUtc: new Date().toISOString(),
      payload: dto,
    },
  });

  return Result.ok({ startedAtUtc });
}
