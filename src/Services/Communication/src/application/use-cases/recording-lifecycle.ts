import type { UserCommunicationPreferencesRepository } from '../ports/user-communication-preferences-repository.js';
import { speakerTranscriptLanguage } from './speaker-transcript-language.js';
import { randomUUID } from 'node:crypto';
import { logger } from '../../infrastructure/logger/logger.js';
import { Result, makeError } from '../../domain/shared/result.js';
import type { RecordingSession } from '../../domain/recording/recording-session.js';
import {
  RecordingCaptureMode,
  RecordingScope,
  RecordingStopReason,
  type RecordingStopReason as RecordingStopReasonType,
} from '../../domain/recording/recording-session-state.js';
import { TranscriptArtifact } from '../../domain/recording/transcript-artifact.js';
import { MeetingEventScope, MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { EffectiveSettingsResolver } from '../ports/effective-settings-resolver.js';
import type { TranscriptArtifactRepository } from '../ports/transcript-artifact-repository.js';
import type { SfuService } from '../ports/sfu-service.js';
import type { RecordingCaptureService } from '../services/recording-capture.service.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';
import {
  MeetingEventTypes,
  type MeetingRecordingFailedEvent,
  type MeetingRecordingLimitReachedEvent,
  type MeetingRecordingProcessingStartedEvent,
  type MeetingRecordingReadyEvent,
  type MeetingRecordingStoppedEvent,
  type RecordingAudioTrackRef,
} from '../../contracts/events/meeting-events.js';
import {
  CallEventTypes,
  type CallRecordingFailedEvent,
  type CallRecordingLimitReachedEvent,
  type CallRecordingProcessingStartedEvent,
  type CallRecordingReadyEvent,
  type CallRecordingStoppedEvent,
} from '../../contracts/events/call-events.js';
import {
  MeetingSocketEvents,
  type MeetingRecordingLimitWarningDto,
  type MeetingRecordingStateChangedDto,
} from '../../contracts/socket/meeting-socket-events.js';
import {
  CallSocketEvents,
  type CallRecordingCaptureRequestedDto,
  type CallRecordingCaptureStoppedDto,
  type CallRecordingLimitWarningDto,
  type CallRecordingStateChangedDto,
} from '../../contracts/socket/call-socket-events.js';
import type { RecordingComposedPayload, RecordingCompositionFailedPayload } from '../../contracts/events/recording-events.js';

/**
 * F7: transiciones de la grabacion que dispara el SISTEMA (captura server-side, limite, fin de la
 * reunion/llamada, recorder, CloudStorage) y sus avisos. Las que dispara una persona (pedir, parar,
 * consentir, adjuntar) siguen en sus use cases y llaman aqui para lo comun.
 */
export interface RecordingLifecycleDeps extends MeetingEventLogDeps {
  readonly meetings: MeetingRepository;
  readonly calls: CallRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter?: RealtimeEmitter | undefined;
  readonly effectiveSettings: Pick<EffectiveSettingsResolver, 'resolve'>;
  readonly capture?: RecordingCaptureService | undefined;
  readonly transcripts?: TranscriptArtifactRepository | undefined;
  readonly sfu?: Pick<SfuService, 'getRouterRtpCapabilities' | 'closeMeeting'> | undefined;
  /** Idioma de voz de cada participante para su pista (INV-T01). */
  readonly userPreferences?: Pick<UserCommunicationPreferencesRepository, 'find'> | undefined;
}

const envelope = <T>(correlationId: string, payload: T) => ({
  eventId: randomUUID(),
  correlationId,
  emittedAtUtc: new Date().toISOString(),
  payload,
});

/** `recording.state_changed` con lo que el front necesita para el REC, la cuenta regresiva y el motivo. */
export function emitRecordingState(
  deps: Pick<RecordingLifecycleDeps, 'emitter'>,
  session: RecordingSession,
  correlationId: string,
  reason?: string | null,
): void {
  if (!deps.emitter) return;
  const s = session.toSnapshot();
  const base = {
    state: s.state,
    updatedAtUtc: new Date().toISOString(),
    captureMode: s.captureMode,
    startedAtUtc: s.startedAtUtc ? s.startedAtUtc.toISOString() : null,
    maxDurationSeconds: s.maxDurationSeconds,
    reason: reason ?? (s.state === 'Stopping' ? s.stopReason : s.state === 'Failed' ? s.failureReason : null),
  };
  if (s.scope === RecordingScope.Meeting) {
    const dto: MeetingRecordingStateChangedDto = { meetingId: s.scopeId, ...base };
    deps.emitter.emitToMeeting({ tenantId: s.tenantId, meetingId: s.scopeId, event: MeetingSocketEvents.RecordingStateChanged, envelope: envelope(correlationId, dto) });
  } else {
    const dto: CallRecordingStateChangedDto = { callId: s.scopeId, ...base };
    deps.emitter.emitToCall({ tenantId: s.tenantId, callId: s.scopeId, event: CallSocketEvents.RecordingStateChanged, envelope: envelope(correlationId, dto) });
  }
}

async function logRecordingEvent(
  deps: RecordingLifecycleDeps,
  session: RecordingSession,
  type: MeetingEventType,
  correlationId: string,
  payload?: Record<string, unknown>,
  actorUserId?: string | null,
): Promise<void> {
  const s = session.toSnapshot();
  await recordMeetingEvent(deps, {
    tenantId: s.tenantId,
    meetingId: s.scopeId,
    scope: s.scope === RecordingScope.Meeting ? MeetingEventScope.Meeting : MeetingEventScope.Call,
    type,
    actorUserId: actorUserId ?? null,
    payload: payload ?? null,
    correlationId,
  });
}

/** Capture mode y limite efectivos al arrancar (T-35, C-03). */
export async function resolveRecordingStart(
  deps: Pick<RecordingLifecycleDeps, 'effectiveSettings' | 'capture'>,
  input: { tenantId: string; scope: RecordingScope; callKind?: 'Audio' | 'Video' },
): Promise<{ captureMode: RecordingCaptureMode; maxDurationSeconds: number }> {
  const effective = await deps.effectiveSettings.resolve(input.tenantId);
  const minutes =
    input.scope === RecordingScope.Meeting
      ? effective.recording.maxMinutesMeeting
      : input.callKind === 'Video'
        ? effective.recording.maxMinutesVideoCall
        : effective.recording.maxMinutesAudioCall;
  return {
    captureMode: effective.recording.serverSide ? RecordingCaptureMode.Server : RecordingCaptureMode.Client,
    maxDurationSeconds: minutes * 60,
  };
}

/**
 * Tras guardar la sesion en Recording: si es server-side, abre el recorder y engancha las pistas.
 * Llamadas (D-56): pide a ambos clientes que publiquen al router de la llamada (solo envio).
 */
export async function beginServerCapture(session: RecordingSession, deps: RecordingLifecycleDeps, correlationId: string): Promise<void> {
  const s = session.toSnapshot();
  // Actor = quien pidió grabar (la ficha muestra "X started recording").
  await logRecordingEvent(
    deps,
    session,
    MeetingEventType.RecordingStarted,
    correlationId,
    { captureMode: s.captureMode, maxDurationSeconds: s.maxDurationSeconds },
    s.requestedByUserId,
  );
  if (s.captureMode !== RecordingCaptureMode.Server) return;
  if (!deps.capture) {
    await failRecording(session, 'RecorderUnavailable', deps, correlationId);
    return;
  }
  const begun = await deps.capture.begin(session);
  if (!begun.isSuccess) {
    await failRecording(session, begun.error.code.replace(/^Recording\./, ''), deps, correlationId);
    return;
  }
  if (s.scope === RecordingScope.Call && deps.emitter && deps.sfu) {
    const effective = await deps.effectiveSettings.resolve(s.tenantId);
    const call = await deps.calls.findById(s.tenantId, s.scopeId);
    const dto: CallRecordingCaptureRequestedDto = {
      callId: s.scopeId,
      routerRtpCapabilities: await deps.sfu.getRouterRtpCapabilities(s.scopeId),
      captureVideo: effective.recording.videoEnabled && call?.toSnapshot().kind === 'Video',
    };
    deps.emitter.emitToCall({ tenantId: s.tenantId, callId: s.scopeId, event: CallSocketEvents.RecordingCaptureRequested, envelope: envelope(correlationId, dto) });
  }
}

/**
 * Tras guardar la sesion en Stopping (usuario, limite o fin): server-side suelta los taps y pide
 * componer → Composing; el cliente (fallback) sube y adjunta por su cuenta.
 */
export async function afterRecordingStopped(session: RecordingSession, deps: RecordingLifecycleDeps, correlationId: string): Promise<void> {
  const s = session.toSnapshot();
  await logRecordingEvent(deps, session, MeetingEventType.RecordingStopped, correlationId, { reason: s.stopReason });
  if (s.captureMode !== RecordingCaptureMode.Server) return;
  if (s.scope === RecordingScope.Call && deps.emitter) {
    const dto: CallRecordingCaptureStoppedDto = { callId: s.scopeId };
    deps.emitter.emitToCall({ tenantId: s.tenantId, callId: s.scopeId, event: CallSocketEvents.RecordingCaptureStopped, envelope: envelope(correlationId, dto) });
  }
  if (!deps.capture) {
    await failRecording(session, 'RecorderUnavailable', deps, correlationId);
    return;
  }
  const stopped = await deps.capture.stop(session, s.stopReason ?? RecordingStopReason.User);
  await closeCallCaptureRouter(deps, s.scope, s.scopeId);
  if (!stopped.isSuccess) {
    await failRecording(session, stopped.error.code.replace(/^Recording\./, ''), deps, correlationId);
    return;
  }
  const composing = session.beginComposing();
  if (!composing.isSuccess) {
    logger.warn({ err: composing.error, sessionId: s.id }, 'beginComposing failed');
    return;
  }
  await deps.recordingSessions.save(session);
  emitRecordingState(deps, session, correlationId);
}

/** D-56: la llamada grabada publica a un router propio (id = callId); se cierra al soltar la captura. */
async function closeCallCaptureRouter(deps: Pick<RecordingLifecycleDeps, 'sfu'>, scope: RecordingScope, scopeId: string): Promise<void> {
  if (scope !== RecordingScope.Call || !deps.sfu) return;
  await deps.sfu.closeMeeting(scopeId).catch((err: unknown) => logger.warn({ err, callId: scopeId }, 'closing call capture router failed'));
}

/** Corte de sistema (limite, fin de la reunion o llamada): sin actor humano. */
export async function stopRecordingBySystem(
  input: { tenantId: string; scope: RecordingScope; scopeId: string; reason: RecordingStopReasonType; correlationId: string },
  deps: RecordingLifecycleDeps,
): Promise<Result<void>> {
  const session = await deps.recordingSessions.findByScope(input.tenantId, input.scope, input.scopeId);
  if (!session || session.sessionState !== 'Recording') return Result.okVoid();
  const now = new Date();
  const stopped = session.stop(now, input.reason);
  if (!stopped.isSuccess) return stopped;
  await deps.recordingSessions.save(session);
  const s = session.toSnapshot();
  const elapsedSeconds = s.startedAtUtc ? Math.max(0, Math.floor((now.getTime() - s.startedAtUtc.getTime()) / 1000)) : 0;
  if (s.scope === RecordingScope.Meeting) {
    const event: MeetingRecordingStoppedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.RecordingStopped,
      tenantId: s.tenantId,
      correlationId: input.correlationId,
      occurredOnUtc: now.toISOString(),
      meetingId: s.scopeId,
      stoppedByUserId: s.requestedByUserId,
      stoppedAtUtc: now.toISOString(),
      elapsedSeconds,
    };
    await deps.publisher.enqueue(event);
  } else {
    const event: CallRecordingStoppedEvent = {
      eventId: randomUUID(),
      eventType: CallEventTypes.RecordingStopped,
      tenantId: s.tenantId,
      correlationId: input.correlationId,
      occurredOnUtc: now.toISOString(),
      callId: s.scopeId,
      stoppedByUserId: s.requestedByUserId,
      stoppedAtUtc: now.toISOString(),
      elapsedSeconds,
    };
    await deps.publisher.enqueue(event);
  }
  if (input.reason === RecordingStopReason.LimitReached) {
    await publishLimitReached(session, deps, input.correlationId, now);
  }
  emitRecordingState(deps, session, input.correlationId);
  await afterRecordingStopped(session, deps, input.correlationId);
  return Result.okVoid();
}

async function publishLimitReached(session: RecordingSession, deps: RecordingLifecycleDeps, correlationId: string, now: Date): Promise<void> {
  const s = session.toSnapshot();
  await logRecordingEvent(deps, session, MeetingEventType.RecordingLimitReached, correlationId, { maxDurationSeconds: s.maxDurationSeconds });
  const common = { eventId: randomUUID(), tenantId: s.tenantId, correlationId, occurredOnUtc: now.toISOString(), maxDurationSeconds: s.maxDurationSeconds ?? 0, stoppedAtUtc: now.toISOString() };
  if (s.scope === RecordingScope.Meeting) {
    const event: MeetingRecordingLimitReachedEvent = { ...common, eventType: MeetingEventTypes.RecordingLimitReached, meetingId: s.scopeId };
    await deps.publisher.enqueue(event);
  } else {
    const event: CallRecordingLimitReachedEvent = { ...common, eventType: CallEventTypes.RecordingLimitReached, callId: s.scopeId };
    await deps.publisher.enqueue(event);
  }
}

/** Aviso unico antes del corte (T-35). */
export async function warnRecordingLimit(session: RecordingSession, deps: RecordingLifecycleDeps, now: Date): Promise<void> {
  const warned = session.warnLimit(now);
  if (!warned.isSuccess) return;
  await deps.recordingSessions.save(session);
  const s = session.toSnapshot();
  const limit = session.limitAtUtc() ?? now;
  const correlationId = `recording-limit:${s.id}`;
  const remainingSeconds = Math.max(0, Math.round((limit.getTime() - now.getTime()) / 1000));
  await logRecordingEvent(deps, session, MeetingEventType.RecordingLimitWarning, correlationId, { remainingSeconds });
  if (!deps.emitter) return;
  if (s.scope === RecordingScope.Meeting) {
    const dto: MeetingRecordingLimitWarningDto = { meetingId: s.scopeId, remainingSeconds, stopsAtUtc: limit.toISOString() };
    deps.emitter.emitToMeeting({ tenantId: s.tenantId, meetingId: s.scopeId, event: MeetingSocketEvents.RecordingLimitWarning, envelope: envelope(correlationId, dto) });
  } else {
    const dto: CallRecordingLimitWarningDto = { callId: s.scopeId, remainingSeconds, stopsAtUtc: limit.toISOString() };
    deps.emitter.emitToCall({ tenantId: s.tenantId, callId: s.scopeId, event: CallSocketEvents.RecordingLimitWarning, envelope: envelope(correlationId, dto) });
  }
}

/** Failed + aviso + evento de integracion (Notification avisa al host). Idempotente con sesiones terminales. */
export async function failRecording(session: RecordingSession, reason: string, deps: RecordingLifecycleDeps, correlationId: string): Promise<void> {
  const before = session.toSnapshot();
  const failed = session.fail({ reason });
  if (!failed.isSuccess) return;
  await deps.recordingSessions.save(session);
  if (before.captureMode === RecordingCaptureMode.Server && deps.capture && (before.state === 'Recording' || before.state === 'Stopping' || before.state === 'Requesting')) {
    await deps.capture.abort(session);
  }
  if (before.captureMode === RecordingCaptureMode.Server && before.scope === RecordingScope.Call) {
    // El cliente suelta su publicador de captura; el router propio de la llamada ya no sirve.
    if (deps.emitter) {
      const dto: CallRecordingCaptureStoppedDto = { callId: before.scopeId };
      deps.emitter.emitToCall({ tenantId: before.tenantId, callId: before.scopeId, event: CallSocketEvents.RecordingCaptureStopped, envelope: envelope(correlationId, dto) });
    }
    await closeCallCaptureRouter(deps, before.scope, before.scopeId);
  }
  await logRecordingEvent(deps, session, MeetingEventType.RecordingFailed, correlationId, { reason });
  const failedAtUtc = new Date().toISOString();
  if (before.scope === RecordingScope.Meeting) {
    const meeting = await deps.meetings.findById(before.tenantId, before.scopeId);
    const event: MeetingRecordingFailedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.RecordingFailed,
      tenantId: before.tenantId,
      correlationId,
      occurredOnUtc: failedAtUtc,
      meetingId: before.scopeId,
      reason,
      failedAtUtc,
      hostUserId: meeting?.hostUserId ?? before.requestedByUserId,
    };
    await deps.publisher.enqueue(event);
  } else {
    const call = await deps.calls.findById(before.tenantId, before.scopeId);
    const snap = call?.toSnapshot();
    const event: CallRecordingFailedEvent = {
      eventId: randomUUID(),
      eventType: CallEventTypes.RecordingFailed,
      tenantId: before.tenantId,
      correlationId,
      occurredOnUtc: failedAtUtc,
      callId: before.scopeId,
      reason,
      failedAtUtc,
      callerUserId: snap?.callerUserId ?? before.requestedByUserId,
      calleeUserId: snap?.calleeUserId ?? before.requestedByUserId,
    };
    await deps.publisher.enqueue(event);
  }
  emitRecordingState(deps, session, correlationId, reason);
}

/**
 * Pide el transcript al worker (si el plan lo incluye): pistas por hablante cuando existen, idioma y
 * vocabulario de la oficina y el limite para su timeout. Crea/reinicia el TranscriptArtifact.
 */
export async function requestTranscript(session: RecordingSession, deps: RecordingLifecycleDeps, correlationId: string): Promise<void> {
  const s = session.toSnapshot();
  if (!s.recordingFileId) return;
  const effective = await deps.effectiveSettings.resolve(s.tenantId);
  if (!effective.recording.transcriptEnabled) return;

  if (deps.transcripts) {
    const existing = await deps.transcripts.findByScope(s.tenantId, s.scope, s.scopeId);
    const artifact = existing ?? TranscriptArtifact.start({ tenantId: s.tenantId, scope: s.scope, scopeId: s.scopeId, recordingSessionId: s.id });
    if (existing) existing.restart();
    await deps.transcripts.save(artifact);
  }

  // INV-T01 R-7: si el host eligió idioma para la reunión, rige para todos; si no, el de cada uno.
  const meetingLanguage =
    s.scope === RecordingScope.Meeting
      ? ((await deps.meetings.findById(s.tenantId, s.scopeId))?.toSnapshot().transcriptLanguage ?? null)
      : null;
  const audioTracks = await audioTrackRefs(session, deps, meetingLanguage);
  const startedAtUtc = new Date().toISOString();
  const extra = {
    ...(audioTracks.length > 0 ? { audioTracks } : {}),
    language: meetingLanguage ?? effective.recording.transcriptLanguage,
    prompt: effective.recording.transcriptPrompt,
    ...(s.maxDurationSeconds ? { maxDurationSeconds: s.maxDurationSeconds } : {}),
    timeoutFactorPct: effective.recording.workerTimeoutFactorPct,
  };
  if (s.scope === RecordingScope.Meeting) {
    const event: MeetingRecordingProcessingStartedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.RecordingProcessingStarted,
      tenantId: s.tenantId,
      correlationId,
      occurredOnUtc: startedAtUtc,
      meetingId: s.scopeId,
      recordingFileId: s.recordingFileId,
      startedAtUtc,
      ...extra,
    };
    await deps.publisher.enqueue(event);
  } else {
    const event: CallRecordingProcessingStartedEvent = {
      eventId: randomUUID(),
      eventType: CallEventTypes.RecordingProcessingStarted,
      tenantId: s.tenantId,
      correlationId,
      occurredOnUtc: startedAtUtc,
      callId: s.scopeId,
      recordingFileId: s.recordingFileId,
      startedAtUtc,
      ...extra,
    };
    await deps.publisher.enqueue(event);
  }
}

/** Pistas del manifest con el nombre del participante (el transcript etiqueta por hablante sin diarizar). */
async function audioTrackRefs(
  session: RecordingSession,
  deps: RecordingLifecycleDeps,
  meetingLanguage: string | null,
): Promise<RecordingAudioTrackRef[]> {
  const s = session.toSnapshot();
  const tracks = s.tracksManifest?.audioTracks ?? [];
  if (tracks.length === 0) return [];
  const names = new Map<string, string>();
  if (s.scope === RecordingScope.Meeting) {
    const meeting = await deps.meetings.findById(s.tenantId, s.scopeId);
    for (const p of meeting?.toSnapshot().participants ?? []) names.set(p.userId, p.displayName);
  } else {
    const call = await deps.calls.findById(s.tenantId, s.scopeId);
    for (const p of call?.toSnapshot().participants ?? []) names.set(p.userId, p.displayName);
  }
  const languages = new Map<string, string | null>();
  for (const userId of new Set(tracks.map((t) => t.userId))) {
    languages.set(userId, meetingLanguage ?? (await speakerTranscriptLanguage(deps.userPreferences, s.tenantId, userId)));
  }
  return tracks.map((t) => {
    // La pantalla puede traer audio en cualquier idioma (un video): siempre auto.
    const language = t.source === 'screen' ? 'auto' : languages.get(t.userId);
    return {
      fileId: t.fileId,
      userId: t.userId,
      displayName: (names.get(t.userId) ?? 'Participant') + (t.source === 'screen' ? ' (screen)' : ''),
      startOffsetMs: t.startOffsetMs,
      ...(language ? { language } : {}),
    };
  });
}

/** Recorder: compuesto subido → Processing; Meeting/Call apuntan al compuesto; se pide el transcript. */
export async function handleRecordingComposed(
  payload: RecordingComposedPayload,
  env: { tenantId: string; correlationId?: string | null },
  deps: RecordingLifecycleDeps,
): Promise<void> {
  const correlationId = env.correlationId ?? `recording:${payload.sessionId}`;
  const session = await deps.recordingSessions.findByScope(env.tenantId, payload.scope, payload.scopeId);
  if (!session || session.id !== payload.sessionId) {
    logger.warn({ sessionId: payload.sessionId }, 'recording.composed for an unknown session');
    return;
  }
  if (session.sessionState === 'Processing' && session.toSnapshot().composedFileId === payload.composedFileId) return;
  const composed = session.composed({
    composedFileId: payload.composedFileId,
    composedSizeBytes: payload.composedSizeBytes,
    format: payload.format,
    durationSeconds: payload.durationSeconds,
    manifest: { audioTracks: payload.audioTracks, videoTrackCount: payload.videoTrackCount },
  });
  if (!composed.isSuccess) {
    logger.warn({ err: composed.error, sessionId: payload.sessionId }, 'recording.composed could not be applied');
    return;
  }
  await deps.recordingSessions.save(session);
  if (payload.scope === RecordingScope.Meeting) {
    const meeting = await deps.meetings.findById(env.tenantId, payload.scopeId);
    if (meeting && meeting.setRecordingFile(payload.composedFileId).isSuccess) await deps.meetings.save(meeting);
  } else {
    const call = await deps.calls.findById(env.tenantId, payload.scopeId);
    if (call && call.setRecordingFile(payload.composedFileId).isSuccess) await deps.calls.save(call);
  }
  emitRecordingState(deps, session, correlationId);
  await requestTranscript(session, deps, correlationId);
}

export async function handleCompositionFailed(
  payload: RecordingCompositionFailedPayload,
  env: { tenantId: string; correlationId?: string | null },
  deps: RecordingLifecycleDeps,
): Promise<void> {
  const session = await deps.recordingSessions.findByScope(env.tenantId, payload.scope, payload.scopeId);
  if (!session || session.id !== payload.sessionId) return;
  logger.warn({ sessionId: payload.sessionId, reason: payload.reason, error: payload.errorMessage }, 'recording composition failed');
  await failRecording(session, payload.reason, deps, env.correlationId ?? `recording:${payload.sessionId}`);
}

/** CloudStorage marco Available el archivo de la grabacion → Ready (T-34) + aviso + recording_ready. */
export async function markRecordingFileAvailable(
  input: { tenantId: string; fileId: string; correlationId?: string | null },
  deps: RecordingLifecycleDeps,
): Promise<void> {
  const session = await deps.recordingSessions.findByRecordingFileId(input.tenantId, input.fileId);
  if (!session || session.sessionState !== 'Processing') return;
  const correlationId = input.correlationId ?? `recording:${session.id}`;
  const s0 = session.toSnapshot();
  if (s0.scope === RecordingScope.Meeting) {
    const meeting = await deps.meetings.findById(input.tenantId, s0.scopeId);
    if (!meeting) return;
    const ready = meeting.markRecordingReady({ session });
    if (!ready.isSuccess) return;
    await deps.recordingSessions.save(session);
    const s = session.toSnapshot();
    const snap = meeting.toSnapshot();
    const event: MeetingRecordingReadyEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.RecordingReady,
      tenantId: input.tenantId,
      correlationId,
      occurredOnUtc: (s.readyAtUtc ?? new Date()).toISOString(),
      meetingId: s.scopeId,
      recordingFileId: s.recordingFileId ?? input.fileId,
      durationSeconds: s.durationSeconds ?? 0,
      participantCount: snap.participants.length,
      readyAtUtc: (s.readyAtUtc ?? new Date()).toISOString(),
      hostUserId: snap.hostUserId,
    };
    await deps.publisher.enqueue(event);
  } else {
    const call = await deps.calls.findById(input.tenantId, s0.scopeId);
    if (!call) return;
    const ready = call.markRecordingReady({ session });
    if (!ready.isSuccess) return;
    await deps.recordingSessions.save(session);
    const s = session.toSnapshot();
    const snap = call.toSnapshot();
    const event: CallRecordingReadyEvent = {
      eventId: randomUUID(),
      eventType: CallEventTypes.RecordingReady,
      tenantId: input.tenantId,
      correlationId,
      occurredOnUtc: (s.readyAtUtc ?? new Date()).toISOString(),
      callId: s.scopeId,
      recordingFileId: s.recordingFileId ?? input.fileId,
      durationSeconds: s.durationSeconds ?? 0,
      readyAtUtc: (s.readyAtUtc ?? new Date()).toISOString(),
      callerUserId: snap.callerUserId,
      calleeUserId: snap.calleeUserId,
    };
    await deps.publisher.enqueue(event);
  }
  await logRecordingEvent(deps, session, MeetingEventType.RecordingReady, correlationId, { fileId: input.fileId });
  emitRecordingState(deps, session, correlationId);
}

/** CloudStorage rechazo el archivo (virus, escaneo fallido, politica, cuota) → Failed. */
export async function markRecordingFileRejected(
  input: { tenantId: string; fileId: string; reason: string; correlationId?: string | null },
  deps: RecordingLifecycleDeps,
): Promise<void> {
  const session = await deps.recordingSessions.findByRecordingFileId(input.tenantId, input.fileId);
  if (!session || session.sessionState !== 'Processing') return;
  await failRecording(session, input.reason, deps, input.correlationId ?? `recording:${session.id}`);
}

/** Recompose (PlatformOnly): vuelve a componer desde el spool del recorder con la politica vigente. */
export async function recomposeRecording(
  input: { tenantId: string; scope: RecordingScope; scopeId: string; correlationId: string },
  deps: RecordingLifecycleDeps,
): Promise<Result<void>> {
  const session = await deps.recordingSessions.findByScope(input.tenantId, input.scope, input.scopeId);
  if (!session) return Result.fail(makeError('Recording.NotFound', 'Recording not found.'));
  if (!deps.capture) return Result.fail(makeError('Recording.RecorderUnavailable', 'The recording service is not available.'));
  const begin = session.beginRecomposing();
  if (!begin.isSuccess) return begin;
  const requested = await deps.capture.recompose(session);
  if (!requested.isSuccess) return requested;
  await deps.recordingSessions.save(session);
  emitRecordingState(deps, session, input.correlationId);
  return Result.okVoid();
}

/** Dependencias opcionales que los use cases de grabacion reenvian (tests viejos no las pasan). */
export interface RecordingLifecycleOptionalDeps extends MeetingEventLogDeps {
  readonly meetings?: MeetingRepository | undefined;
  readonly calls?: CallRepository | undefined;
  readonly effectiveSettings?: Pick<EffectiveSettingsResolver, 'resolve'> | undefined;
  readonly capture?: RecordingCaptureService | undefined;
  readonly transcripts?: TranscriptArtifactRepository | undefined;
  readonly sfu?: Pick<SfuService, 'getRouterRtpCapabilities' | 'closeMeeting'> | undefined;
  readonly userPreferences?: Pick<UserCommunicationPreferencesRepository, 'find'> | undefined;
}

/** Arma las deps del ciclo de vida si estan completas; sin resolver/repositorios = comportamiento previo a F7. */
export function lifecycleFrom(
  deps: RecordingLifecycleOptionalDeps & {
    recordingSessions: RecordingSessionRepository;
    publisher: IntegrationEventPublisher;
    emitter?: RealtimeEmitter | undefined;
  },
): RecordingLifecycleDeps | null {
  if (!deps.effectiveSettings || !deps.meetings || !deps.calls) return null;
  return {
    meetings: deps.meetings,
    calls: deps.calls,
    recordingSessions: deps.recordingSessions,
    publisher: deps.publisher,
    emitter: deps.emitter,
    effectiveSettings: deps.effectiveSettings,
    capture: deps.capture,
    transcripts: deps.transcripts,
    sfu: deps.sfu,
    meetingEvents: deps.meetingEvents,
    userPreferences: deps.userPreferences,
  };
}

/**
 * Fin de la reunion o llamada con grabacion activa: corte de sistema best-effort (nunca rompe el cierre).
 * Sin las deps del ciclo de vida (tests viejos) no hace nada.
 */
export async function stopRecordingOnEnd(
  deps: RecordingLifecycleOptionalDeps & {
    recordingSessions?: RecordingSessionRepository | undefined;
    publisher: IntegrationEventPublisher;
    emitter?: RealtimeEmitter | undefined;
  },
  input: { tenantId: string; scope: RecordingScope; scopeId: string; reason: RecordingStopReasonType; correlationId: string },
): Promise<void> {
  if (!deps.recordingSessions) return;
  const lifecycle = lifecycleFrom({ ...deps, recordingSessions: deps.recordingSessions });
  if (!lifecycle) return;
  await stopRecordingBySystem(input, lifecycle).catch((err: unknown) =>
    logger.warn({ err, scope: input.scope, scopeId: input.scopeId }, 'stop recording on end failed'),
  );
}
