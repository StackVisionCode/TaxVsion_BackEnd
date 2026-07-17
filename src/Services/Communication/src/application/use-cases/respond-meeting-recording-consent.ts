import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { RecordingConsentEntryStatus } from '../../domain/recording/recording-consent.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RecordingSessionRepository, RecordingConsentRepository } from '../ports/recording-repository.js';
import type { SettingsRepository } from '../ports/settings-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  MeetingEventTypes,
  type MeetingRecordingConsentRecordedEvent,
} from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingRecordingStateChangedDto,
  type MeetingRecordingConsentRecordedDto,
} from '../../contracts/socket/meeting-socket-events.js';
import { startMeetingRecording } from './start-meeting-recording.js';
import { logger } from '../../infrastructure/logger/logger.js';

export interface RespondMeetingRecordingConsentCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly actorUserId: string;
  readonly response: 'Accepted' | 'Rejected';
}

/** Superset de StartMeetingRecordingDeps — se reenvia tal cual al auto-invoke. */
export interface RespondMeetingRecordingConsentDeps {
  readonly meetings: MeetingRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly recordingConsents: RecordingConsentRepository;
  readonly tenantSettings: SettingsRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

export async function respondMeetingRecordingConsent(
  command: RespondMeetingRecordingConsentCommand,
  deps: RespondMeetingRecordingConsentDeps,
): Promise<Result<{ response: RecordingConsentEntryStatus }>> {
  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const session = await deps.recordingSessions.findByScope(command.tenantId, RecordingScope.Meeting, command.meetingId);
  if (!session) return Result.fail(makeError('RecordingSession.NotFound', 'No recording session for this meeting.'));
  if (session.sessionState !== 'Requesting') {
    return Result.fail(
      makeError('RecordingSession.NotRequesting', `Cannot respond while session is ${session.sessionState}.`),
    );
  }

  const now = new Date();
  const consentResult = meeting.recordConsent({ userId: command.actorUserId, response: command.response, respondedAtUtc: now });
  if (!consentResult.isSuccess) return Result.fail(consentResult.error);
  await deps.recordingConsents.append(consentResult.value);

  const event: MeetingRecordingConsentRecordedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.RecordingConsentRecorded,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: command.meetingId,
    userId: command.actorUserId,
    response: command.response,
    respondedAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);

  // Fase Frontend 3 — a diferencia del "nudge" de abajo (que solo dice que
  // el estado agregado puede haber cambiado), esto SI lleva quien respondio
  // y que dijo, para que cualquier participante pueda armar una UI real de
  // "quien acepto/rechazo" sin esperar a que el estado final cambie.
  const consentRecordedDto: MeetingRecordingConsentRecordedDto = {
    meetingId: command.meetingId,
    userId: command.actorUserId,
    response: command.response,
    respondedAtUtc: now.toISOString(),
  };
  deps.emitter.emitToMeeting({
    tenantId: command.tenantId,
    meetingId: command.meetingId,
    event: MeetingSocketEvents.RecordingConsentRecorded,
    envelope: {
      eventId: randomUUID(),
      correlationId: command.correlationId,
      emittedAtUtc: new Date().toISOString(),
      payload: consentRecordedDto,
    },
  });

  // "Emite socket state update" — el estado en si (Requesting) puede no
  // cambiar aca; el nudge le dice al cliente que refresque quien respondio.
  // Si el auto-start de abajo si consigue arrancar, ese use case emite su
  // propio state_changed(Recording) inmediatamente despues de este.
  const stateDto: MeetingRecordingStateChangedDto = {
    meetingId: command.meetingId,
    state: session.sessionState,
    updatedAtUtc: now.toISOString(),
  };
  deps.emitter.emitToMeeting({
    tenantId: command.tenantId,
    meetingId: command.meetingId,
    event: MeetingSocketEvents.RecordingStateChanged,
    envelope: {
      eventId: randomUUID(),
      correlationId: command.correlationId,
      emittedAtUtc: new Date().toISOString(),
      payload: stateDto,
    },
  });

  // Auto-invoke: si la policy ya queda satisfecha con esta respuesta, arranca
  // sin esperar una accion explicita del host. Un fallo aca (policy todavia no
  // satisfecha, u otro request ya la arranco) es esperable y no es un error
  // para quien esta respondiendo — su voto SI se guardo arriba.
  const startResult = await startMeetingRecording(
    { tenantId: command.tenantId, correlationId: command.correlationId, meetingId: command.meetingId, actorUserId: session.requestedByUserId },
    deps,
  );
  if (!startResult.isSuccess) {
    logger.debug({ err: startResult.error, meetingId: command.meetingId }, 'Auto-start after consent response did not proceed');
  }

  return Result.ok({ response: command.response });
}
