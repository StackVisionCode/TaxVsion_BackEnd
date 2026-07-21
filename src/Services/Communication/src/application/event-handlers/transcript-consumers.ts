import { randomUUID } from 'node:crypto';
import type { CallRepository } from '../ports/call-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { attachCallTranscript } from '../use-cases/attach-call-transcript.js';
import { attachMeetingTranscript } from '../use-cases/attach-meeting-transcript.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import {
  CallSocketEvents,
  type CallTranscriptReadyDto,
  type CallRecordingStateChangedDto,
} from '../../contracts/socket/call-socket-events.js';
import {
  MeetingSocketEvents,
  type MeetingTranscriptReadyDto,
  type MeetingRecordingStateChangedDto,
} from '../../contracts/socket/meeting-socket-events.js';
import { MeetingEventTypes, type MeetingRecordingFailedEvent } from '../../contracts/events/meeting-events.js';
import { CallEventTypes, type CallRecordingFailedEvent } from '../../contracts/events/call-events.js';
import { logger } from '../../infrastructure/logger/logger.js';

/**
 * Consumers de `communication.call.transcript_ready.v1` /
 * `.meeting.transcript_ready.v1` — publicados por el worker de transcripts
 * (proceso separado, Fase 6) al terminar de transcribir una grabacion con
 * whisper.cpp. Adjunta el resultado al aggregate y notifica por socket a los
 * participantes que ya salieron del room del call/meeting (sigue existiendo
 * `t:{tenant}:call:{id}` / `t:{tenant}:m:{id}` mientras haya algun listener,
 * pero el caso comun es notificar a quien vuelva a abrir el historial luego).
 *
 * @since Fase Transcript 3 (worker) — estos handlers YA NO re-publican
 * `communication.{call,meeting}.recording_ready.v1` al completar: ese
 * re-publish (Fase Backend 3/4) resulto ser el UNICO trigger que el flujo
 * con RecordingSession/consent tenia hacia el worker, publicado DESPUES de
 * que el worker ya habia transcrito — circular e imposible como arranque
 * real. El worker ahora arranca correctamente escuchando
 * `recording_processing_started.v1` (ya publicado por attach-*-recording.ts
 * en el momento correcto, sin cambios ahi). Mantener el re-publish aca
 * hubiera hecho que el worker lo recibiera de nuevo (misma cola fanout) y
 * re-transcribiera una grabacion ya lista. Confirmado por grep en todo el
 * repo: ningun otro consumer (.NET o Node) escuchaba `recording_ready.v1`
 * fuera de este worker, asi que remover el re-publish no rompe nada mas.
 */
export function bindTranscriptConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    calls: CallRepository;
    meetings: MeetingRepository;
    recordingSessions: RecordingSessionRepository;
    publisher: IntegrationEventPublisher;
    emitter: RealtimeEmitter;
  },
): void {
  register('communication.call.transcript_ready.v1', async (env) => {
    const callId = getString(env.payload, 'callId');
    const transcriptFileId = getString(env.payload, 'transcriptFileId');
    const detectedLanguage = getString(env.payload, 'detectedLanguage');
    // Fase Transcript 5 — duracion/wordCount son del AUDIO transcripto (via
    // whisper.cpp), distinto de `callDurationSeconds` mas abajo (duracion de
    // la RecordingSession, startedAtUtc->stoppedAtUtc) — nombres distintos a
    // proposito para no confundirlos.
    const transcriptDurationSeconds = getNumber(env.payload, 'durationSeconds') ?? 0;
    const transcriptWordCount = getNumber(env.payload, 'wordCount') ?? 0;
    if (!callId || !transcriptFileId) return;

    const result = await attachCallTranscript(
      { tenantId: env.tenantId, callId, transcriptFileId },
      { calls: deps.calls },
    );
    if (!result.isSuccess) {
      logger.warn({ err: result.error, callId }, 'attachCallTranscript failed');
      return;
    }

    const dto: CallTranscriptReadyDto = {
      callId,
      transcriptFileId,
      detectedLanguage: detectedLanguage ?? null,
      durationSeconds: transcriptDurationSeconds,
      wordCount: transcriptWordCount,
      readyAtUtc: new Date().toISOString(),
    };
    deps.emitter.emitToCall({
      tenantId: env.tenantId,
      callId,
      event: CallSocketEvents.TranscriptReady,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: dto,
      },
    });

    // Fase Backend 4 — mismo patron que meetings: si la call paso por el flujo
    // de consent, la sesion esta en Processing en este punto
    // (attach-call-recording.ts la puso ahi). Calls legacy sin session (el
    // attachCallTranscript de arriba ya corrio igual) no tienen nada que completar.
    const callSession = await deps.recordingSessions.findByScope(env.tenantId, RecordingScope.Call, callId);
    if (!callSession || callSession.sessionState !== 'Processing') return;

    const call = await deps.calls.findById(env.tenantId, callId);
    if (!call) return;

    const callSessionSnap = callSession.toSnapshot();
    const callDurationSeconds =
      callSessionSnap.startedAtUtc && callSessionSnap.stoppedAtUtc
        ? Math.max(0, Math.floor((callSessionSnap.stoppedAtUtc.getTime() - callSessionSnap.startedAtUtc.getTime()) / 1000))
        : 0;
    const callRecordingFileId = call.toSnapshot().recordingFileId;
    if (!callRecordingFileId) return;

    const callCompleteResult = call.completeRecording({ session: callSession, recordingFileId: callRecordingFileId, durationSeconds: callDurationSeconds });
    if (!callCompleteResult.isSuccess) {
      logger.warn({ err: callCompleteResult.error, callId }, 'completeRecording failed after transcript_ready');
      return;
    }
    await deps.recordingSessions.save(callCompleteResult.value);

    const callReadyAtUtc = new Date().toISOString();
    const callStateDto: CallRecordingStateChangedDto = { callId, state: 'Ready', updatedAtUtc: callReadyAtUtc };
    deps.emitter.emitToCall({
      tenantId: env.tenantId,
      callId,
      event: CallSocketEvents.RecordingStateChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: callStateDto,
      },
    });
  });

  register('communication.meeting.transcript_ready.v1', async (env) => {
    const meetingId = getString(env.payload, 'meetingId');
    const transcriptFileId = getString(env.payload, 'transcriptFileId');
    const detectedLanguage = getString(env.payload, 'detectedLanguage');
    // Fase Transcript 5 — duracion/wordCount son del AUDIO transcripto (via
    // whisper.cpp), distinto de `durationSeconds` mas abajo (duracion de la
    // RecordingSession, startedAtUtc->stoppedAtUtc) — nombres distintos a
    // proposito para no confundirlos.
    const transcriptDurationSeconds = getNumber(env.payload, 'durationSeconds') ?? 0;
    const transcriptWordCount = getNumber(env.payload, 'wordCount') ?? 0;
    if (!meetingId || !transcriptFileId) return;

    const result = await attachMeetingTranscript(
      { tenantId: env.tenantId, meetingId, transcriptFileId },
      { meetings: deps.meetings },
    );
    if (!result.isSuccess) {
      logger.warn({ err: result.error, meetingId }, 'attachMeetingTranscript failed');
      return;
    }

    const dto: MeetingTranscriptReadyDto = {
      meetingId,
      transcriptFileId,
      detectedLanguage: detectedLanguage ?? null,
      durationSeconds: transcriptDurationSeconds,
      wordCount: transcriptWordCount,
      readyAtUtc: new Date().toISOString(),
    };
    deps.emitter.emitToMeeting({
      tenantId: env.tenantId,
      meetingId,
      event: MeetingSocketEvents.TranscriptReady,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: dto,
      },
    });

    // Fase Backend 3 — si el meeting paso por el flujo de consent, la sesion
    // esta en Processing en este punto (attach-meeting-recording.ts la puso
    // ahi). Completar aca es lo unico que la mueve a Ready — meetings legacy
    // sin session (attachMeetingTranscript ya corrio arriba igual) no tienen
    // nada que completar.
    const session = await deps.recordingSessions.findByScope(env.tenantId, RecordingScope.Meeting, meetingId);
    if (!session || session.sessionState !== 'Processing') return;

    const meeting = await deps.meetings.findById(env.tenantId, meetingId);
    if (!meeting) return;

    const sessionSnap = session.toSnapshot();
    const durationSeconds =
      sessionSnap.startedAtUtc && sessionSnap.stoppedAtUtc
        ? Math.max(0, Math.floor((sessionSnap.stoppedAtUtc.getTime() - sessionSnap.startedAtUtc.getTime()) / 1000))
        : 0;
    const recordingFileId = meeting.toSnapshot().recordingFileId;
    if (!recordingFileId) return;

    const completeResult = meeting.completeRecording({ session, recordingFileId, durationSeconds });
    if (!completeResult.isSuccess) {
      logger.warn({ err: completeResult.error, meetingId }, 'completeRecording failed after transcript_ready');
      return;
    }
    await deps.recordingSessions.save(completeResult.value);

    const readyAtUtc = new Date().toISOString();
    const stateDto: MeetingRecordingStateChangedDto = { meetingId, state: 'Ready', updatedAtUtc: readyAtUtc };
    deps.emitter.emitToMeeting({
      tenantId: env.tenantId,
      meetingId,
      event: MeetingSocketEvents.RecordingStateChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: stateDto,
      },
    });
  });

  // Publicado por el worker de transcripts cuando ffmpeg/whisper fallan (ej.
  // "no audio stream" — ver bug #245). Solo tiene sentido si el meeting paso
  // por el flujo de consent (session en Processing); meetings legacy no
  // tienen session que fallar.
  register('communication.meeting.transcript_failed.v1', async (env) => {
    const meetingId = getString(env.payload, 'meetingId');
    const reason = getString(env.payload, 'failureReason') ?? getString(env.payload, 'errorMessage') ?? 'TranscriptFailed';
    if (!meetingId) return;

    const session = await deps.recordingSessions.findByScope(env.tenantId, RecordingScope.Meeting, meetingId);
    if (!session || session.sessionState !== 'Processing') return;

    const meeting = await deps.meetings.findById(env.tenantId, meetingId);
    if (!meeting) return;

    const failResult = meeting.failRecording({ session, reason });
    if (!failResult.isSuccess) {
      logger.warn({ err: failResult.error, meetingId }, 'failRecording failed after transcript_failed');
      return;
    }
    await deps.recordingSessions.save(failResult.value);
    await deps.publisher.enqueue(buildMeetingRecordingFailedEvent(env, meetingId, reason, meeting.hostUserId));

    const stateDto: MeetingRecordingStateChangedDto = { meetingId, state: 'Failed', updatedAtUtc: new Date().toISOString() };
    deps.emitter.emitToMeeting({
      tenantId: env.tenantId,
      meetingId,
      event: MeetingSocketEvents.RecordingStateChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: stateDto,
      },
    });
  });

  // Publicado por el worker de transcripts cuando ffmpeg/whisper fallan (ej.
  // "no audio stream" — ver bug #245). Solo tiene sentido si la call paso
  // por el flujo de consent (session en Processing); calls legacy no tienen
  // session que fallar.
  register('communication.call.transcript_failed.v1', async (env) => {
    const callId = getString(env.payload, 'callId');
    const reason = getString(env.payload, 'failureReason') ?? getString(env.payload, 'errorMessage') ?? 'TranscriptFailed';
    if (!callId) return;

    const session = await deps.recordingSessions.findByScope(env.tenantId, RecordingScope.Call, callId);
    if (!session || session.sessionState !== 'Processing') return;

    const call = await deps.calls.findById(env.tenantId, callId);
    if (!call) return;

    const failResult = call.failRecording({ session, reason });
    if (!failResult.isSuccess) {
      logger.warn({ err: failResult.error, callId }, 'failRecording failed after transcript_failed');
      return;
    }
    await deps.recordingSessions.save(failResult.value);
    await deps.publisher.enqueue(
      buildCallRecordingFailedEvent(env, callId, reason, call.callerUserId, call.calleeUserId),
    );

    const stateDto: CallRecordingStateChangedDto = { callId, state: 'Failed', updatedAtUtc: new Date().toISOString() };
    deps.emitter.emitToCall({
      tenantId: env.tenantId,
      callId,
      event: CallSocketEvents.RecordingStateChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: stateDto,
      },
    });
  });

  // Fase Backend 8 (bug #245) — el worker publica esto ANTES de arrancar el
  // transcode cuando ffprobe detecta que el file no tiene track de audio (o
  // esta corrupto). El fix real del bug vive en el frontend (evitar subir un
  // MediaRecorder sin audio track); estos 2 consumers solo se aseguran de
  // que la RecordingSession refleje el estado correcto y el frontend reciba
  // `state_changed:Failed` con un `FailureReason` humano-lectura, en vez de
  // dejar la session colgada en Processing por siempre.

  register('communication.meeting.recording_validation_failed.v1', async (env) => {
    const meetingId = getString(env.payload, 'meetingId');
    const reason = getString(env.payload, 'failureReason') ?? 'NoAudioStream';
    if (!meetingId) return;

    const session = await deps.recordingSessions.findByScope(env.tenantId, RecordingScope.Meeting, meetingId);
    // A diferencia de transcript_failed (que solo actua sobre Processing), la
    // validation ocurre ANTES del transcode y puede llegar en Processing
    // (attach ya migro la session) O en Stopping (el worker corrio antes que
    // attach — caso raro pero posible con retries). Aceptamos ambos.
    if (!session || (session.sessionState !== 'Processing' && session.sessionState !== 'Stopping')) return;

    const meeting = await deps.meetings.findById(env.tenantId, meetingId);
    if (!meeting) return;

    const failResult = meeting.failRecording({ session, reason });
    if (!failResult.isSuccess) {
      logger.warn({ err: failResult.error, meetingId, reason }, 'failRecording failed after validation_failed');
      return;
    }
    await deps.recordingSessions.save(failResult.value);
    await deps.publisher.enqueue(buildMeetingRecordingFailedEvent(env, meetingId, reason, meeting.hostUserId));

    const stateDto: MeetingRecordingStateChangedDto = { meetingId, state: 'Failed', updatedAtUtc: new Date().toISOString() };
    deps.emitter.emitToMeeting({
      tenantId: env.tenantId,
      meetingId,
      event: MeetingSocketEvents.RecordingStateChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: stateDto,
      },
    });
  });

  register('communication.call.recording_validation_failed.v1', async (env) => {
    const callId = getString(env.payload, 'callId');
    const reason = getString(env.payload, 'failureReason') ?? 'NoAudioStream';
    if (!callId) return;

    const session = await deps.recordingSessions.findByScope(env.tenantId, RecordingScope.Call, callId);
    if (!session || (session.sessionState !== 'Processing' && session.sessionState !== 'Stopping')) return;

    const call = await deps.calls.findById(env.tenantId, callId);
    if (!call) return;

    const failResult = call.failRecording({ session, reason });
    if (!failResult.isSuccess) {
      logger.warn({ err: failResult.error, callId, reason }, 'failRecording failed after validation_failed');
      return;
    }
    await deps.recordingSessions.save(failResult.value);
    await deps.publisher.enqueue(
      buildCallRecordingFailedEvent(env, callId, reason, call.callerUserId, call.calleeUserId),
    );

    const stateDto: CallRecordingStateChangedDto = { callId, state: 'Failed', updatedAtUtc: new Date().toISOString() };
    deps.emitter.emitToCall({
      tenantId: env.tenantId,
      callId,
      event: CallSocketEvents.RecordingStateChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: stateDto,
      },
    });
  });
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}

function getNumber(source: Record<string, unknown>, key: string): number | undefined {
  const value = source[key];
  if (typeof value === 'number') return value;
  if (typeof value === 'string') {
    const parsed = Number.parseInt(value, 10);
    if (Number.isFinite(parsed)) return parsed;
  }
  return undefined;
}

// Fase Backend 10 — construyen el `communication.{meeting,call}.recording_failed.v1`
// publicado en cada camino de este archivo que transiciona una RecordingSession
// a Failed (TranscriptFailed y NoAudioStream/validation_failed). Ver docblock
// de MeetingRecordingFailedEvent para el porque de un evento generico separado
// de RecordingValidationFailedEvent.
function buildMeetingRecordingFailedEvent(
  env: IncomingEnvelope,
  meetingId: string,
  reason: string,
  hostUserId: string,
): MeetingRecordingFailedEvent {
  const failedAtUtc = new Date().toISOString();
  return {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.RecordingFailed,
    tenantId: env.tenantId,
    correlationId: env.correlationId ?? '',
    occurredOnUtc: failedAtUtc,
    meetingId,
    reason,
    failedAtUtc,
    hostUserId,
  };
}

function buildCallRecordingFailedEvent(
  env: IncomingEnvelope,
  callId: string,
  reason: string,
  callerUserId: string,
  calleeUserId: string,
): CallRecordingFailedEvent {
  const failedAtUtc = new Date().toISOString();
  return {
    eventId: randomUUID(),
    eventType: CallEventTypes.RecordingFailed,
    tenantId: env.tenantId,
    correlationId: env.correlationId ?? '',
    occurredOnUtc: failedAtUtc,
    callId,
    reason,
    failedAtUtc,
    callerUserId,
    calleeUserId,
  };
}
