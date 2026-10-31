import type { AttachmentTrackingRepository } from '../ports/attachment-tracking-repository.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import { randomUUID } from 'node:crypto';
import type { CallRepository } from '../ports/call-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import type { TranscriptArtifactRepository } from '../ports/transcript-artifact-repository.js';
import type { ConversationRepository } from '../ports/conversation-repository.js';
import type { MessageRepository } from '../ports/message-repository.js';
import { attachCallTranscript } from '../use-cases/attach-call-transcript.js';
import { attachMeetingTranscript } from '../use-cases/attach-meeting-transcript.js';
import { appendCallSystemMessage } from '../use-cases/append-call-system-message.js';
import { RecordingCaptureMode, RecordingScope } from '../../domain/recording/recording-session-state.js';
import { TranscriptArtifact, type TranscriptScope } from '../../domain/recording/transcript-artifact.js';
import type { MessageTranscriptState } from '../../domain/conversations/message.js';
import { CallSocketEvents, type CallTranscriptReadyDto } from '../../contracts/socket/call-socket-events.js';
import { MeetingSocketEvents, type MeetingTranscriptReadyDto } from '../../contracts/socket/meeting-socket-events.js';
import { ChatSocketEvents, type MessageTranscriptUpdatedDto } from '../../contracts/socket/chat-socket-events.js';
import { ChatEventTypes } from '../../contracts/events/chat-events.js';
import { failRecording, type RecordingLifecycleDeps } from '../use-cases/recording-lifecycle.js';
import { logger } from '../../infrastructure/logger/logger.js';

/**
 * Consumers del worker de transcripts (proceso separado).
 *
 * F7 (T-34): el transcript tiene su propio estado (TranscriptArtifact) y YA NO decide el de la
 * grabacion — Ready lo marca la disponibilidad del archivo en CloudStorage (recording-lifecycle.ts).
 * El v2 trae, ademas del .txt, un .json con hablantes (T-28); en llamadas se deja en el hilo 1:1 un
 * mensaje System con el transcript adjunto (D-27). Las notas de voz vuelven con el texto en linea.
 */
export interface TranscriptConsumerDeps {
  readonly calls: CallRepository;
  readonly meetings: MeetingRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
  readonly transcripts?: TranscriptArtifactRepository | undefined;
  readonly conversations?: ConversationRepository | undefined;
  readonly messages?: MessageRepository | undefined;
  /** El transcript adjunto al mensaje System se registra como adjunto del chat (descarga del portal). */
  readonly attachmentTracking?: AttachmentTrackingRepository | undefined;
  readonly cloudStorageMetadata?: CloudStorageMetadataClient | undefined;
  /** Para fallar grabaciones del camino cliente (archivo sin audio). */
  readonly lifecycle?: RecordingLifecycleDeps | null | undefined;
}

export function bindTranscriptConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: TranscriptConsumerDeps,
): void {
  register('communication.call.transcript_ready.v1', async (env) => {
    const callId = getString(env.payload, 'callId');
    const transcriptFileId = getString(env.payload, 'transcriptFileId');
    if (!callId || !transcriptFileId) return;
    const v2 = readTranscriptV2(env.payload);
    // Una recomposición (o la reentrega del evento) vuelve a transcribir: el aviso en el chat va una sola vez.
    const hadTranscript = !!(await deps.calls.findById(env.tenantId, callId))?.toSnapshot().transcriptFileId;

    const result = await attachCallTranscript({ tenantId: env.tenantId, callId, transcriptFileId }, { calls: deps.calls });
    if (!result.isSuccess) {
      logger.warn({ err: result.error, callId }, 'attachCallTranscript failed');
      return;
    }
    await completeArtifact(deps, env.tenantId, 'Call', callId, transcriptFileId, v2);

    const dto: CallTranscriptReadyDto = {
      callId,
      transcriptFileId,
      detectedLanguage: v2.language,
      durationSeconds: v2.durationSeconds,
      wordCount: v2.wordCount,
      readyAtUtc: new Date().toISOString(),
      transcriptJsonFileId: v2.jsonFileId,
      speakerCount: v2.speakerCount,
      segmentCount: v2.segmentCount,
      language: v2.language,
    };
    deps.emitter.emitToCall({ tenantId: env.tenantId, callId, event: CallSocketEvents.TranscriptReady, envelope: envelope(env, dto) });

    // D-27: el transcript queda en el hilo de la llamada como enlace (descarga por la membresia del chat).
    const call = await deps.calls.findById(env.tenantId, callId);
    const conversationId = call?.toSnapshot().conversationId;
    if (conversationId && deps.conversations && !hadTranscript) {
      await appendCallSystemMessage(
        { tenantId: env.tenantId, conversationId, body: 'Call transcript is ready', attachmentFileId: transcriptFileId },
        {
          conversations: deps.conversations,
          emitter: deps.emitter,
          attachmentTracking: deps.attachmentTracking,
          cloudStorageMetadata: deps.cloudStorageMetadata,
        },
      ).catch((err: unknown) => logger.warn({ err, callId }, 'call transcript system message failed'));
    }
  });

  register('communication.meeting.transcript_ready.v1', async (env) => {
    const meetingId = getString(env.payload, 'meetingId');
    const transcriptFileId = getString(env.payload, 'transcriptFileId');
    if (!meetingId || !transcriptFileId) return;
    const v2 = readTranscriptV2(env.payload);

    const result = await attachMeetingTranscript({ tenantId: env.tenantId, meetingId, transcriptFileId }, { meetings: deps.meetings });
    if (!result.isSuccess) {
      logger.warn({ err: result.error, meetingId }, 'attachMeetingTranscript failed');
      return;
    }
    await completeArtifact(deps, env.tenantId, 'Meeting', meetingId, transcriptFileId, v2);

    const dto: MeetingTranscriptReadyDto = {
      meetingId,
      transcriptFileId,
      detectedLanguage: v2.language,
      durationSeconds: v2.durationSeconds,
      wordCount: v2.wordCount,
      readyAtUtc: new Date().toISOString(),
      transcriptJsonFileId: v2.jsonFileId,
      speakerCount: v2.speakerCount,
      segmentCount: v2.segmentCount,
      language: v2.language,
    };
    deps.emitter.emitToMeeting({ tenantId: env.tenantId, meetingId, event: MeetingSocketEvents.TranscriptReady, envelope: envelope(env, dto) });
  });

  // El transcript fallido ya no tumba la grabacion (H-5): solo su propio estado.
  for (const [eventType, scope, idField] of [
    ['communication.meeting.transcript_failed.v1', 'Meeting', 'meetingId'],
    ['communication.call.transcript_failed.v1', 'Call', 'callId'],
  ] as const) {
    register(eventType, async (env) => {
      const scopeId = getString(env.payload, idField);
      const reason = getString(env.payload, 'failureReason') ?? 'TranscriptFailed';
      if (!scopeId) return;
      logger.warn({ scope, scopeId, reason, error: getString(env.payload, 'errorMessage') }, 'transcript failed');
      if (!deps.transcripts) return;
      const artifact =
        (await deps.transcripts.findByScope(env.tenantId, scope, scopeId)) ??
        TranscriptArtifact.start({ tenantId: env.tenantId, scope, scopeId });
      artifact.fail(reason);
      await deps.transcripts.save(artifact);
    });
  }

  // Camino cliente (fallback C-03): un archivo sin pista de audio sigue fallando la grabacion.
  for (const [eventType, scope, idField] of [
    ['communication.meeting.recording_validation_failed.v1', RecordingScope.Meeting, 'meetingId'],
    ['communication.call.recording_validation_failed.v1', RecordingScope.Call, 'callId'],
  ] as const) {
    register(eventType, async (env) => {
      const scopeId = getString(env.payload, idField);
      const reason = getString(env.payload, 'failureReason') ?? 'NoAudioStream';
      if (!scopeId || !deps.lifecycle) return;
      const session = await deps.recordingSessions.findByScope(env.tenantId, scope, scopeId);
      if (!session || session.captureMode !== RecordingCaptureMode.Client) return;
      if (session.sessionState !== 'Processing' && session.sessionState !== 'Stopping') return;
      await failRecording(session, reason, deps.lifecycle, env.correlationId ?? `recording:${session.id}`);
    });
  }

  // D-27: notas de voz — el texto vuelve en linea y se guarda en el mensaje.
  register(ChatEventTypes.VoiceNoteTranscriptReady, async (env) => {
    const messageId = getString(env.payload, 'messageId');
    if (!messageId) return;
    await updateVoiceNote(deps, env, messageId, 'Ready', getString(env.payload, 'transcriptText') ?? null, getString(env.payload, 'detectedLanguage') ?? null);
  });

  register(ChatEventTypes.VoiceNoteTranscriptFailed, async (env) => {
    const messageId = getString(env.payload, 'messageId');
    if (!messageId) return;
    logger.warn({ messageId, reason: getString(env.payload, 'failureReason') }, 'voice note transcript failed');
    await updateVoiceNote(deps, env, messageId, 'Failed', null, null);
  });
}

interface TranscriptV2 {
  readonly jsonFileId: string | null;
  readonly language: string | null;
  readonly durationSeconds: number;
  readonly wordCount: number;
  readonly segmentCount: number;
  readonly speakerCount: number;
  readonly model: string | null;
  readonly vad: boolean;
  readonly realtimeFactor: number | null;
}

/** Campos v2 opcionales: un worker previo a F7 manda solo los del v1. */
function readTranscriptV2(payload: Record<string, unknown>): TranscriptV2 {
  return {
    jsonFileId: getString(payload, 'transcriptJsonFileId') ?? null,
    language: getString(payload, 'detectedLanguage') ?? null,
    durationSeconds: getNumber(payload, 'durationSeconds') ?? 0,
    wordCount: getNumber(payload, 'wordCount') ?? 0,
    segmentCount: getNumber(payload, 'segmentCount') ?? 0,
    speakerCount: getNumber(payload, 'speakerCount') ?? 0,
    model: getString(payload, 'model') ?? null,
    vad: payload['vad'] === true,
    realtimeFactor: typeof payload['realtimeFactor'] === 'number' ? payload['realtimeFactor'] : null,
  };
}

async function completeArtifact(
  deps: TranscriptConsumerDeps,
  tenantId: string,
  scope: TranscriptScope,
  scopeId: string,
  txtFileId: string,
  v2: TranscriptV2,
): Promise<void> {
  if (!deps.transcripts) return;
  const artifact =
    (await deps.transcripts.findByScope(tenantId, scope, scopeId)) ?? TranscriptArtifact.start({ tenantId, scope, scopeId });
  const done = artifact.complete({
    txtFileId,
    jsonFileId: v2.jsonFileId,
    language: v2.language,
    model: v2.model,
    vad: v2.vad,
    durationSeconds: v2.durationSeconds,
    wordCount: v2.wordCount,
    segmentCount: v2.segmentCount,
    speakerCount: v2.speakerCount,
    realtimeFactor: v2.realtimeFactor,
  });
  if (!done.isSuccess) {
    logger.warn({ err: done.error, scope, scopeId }, 'transcript artifact could not complete');
    return;
  }
  await deps.transcripts.save(artifact);
}

async function updateVoiceNote(
  deps: TranscriptConsumerDeps,
  env: IncomingEnvelope,
  messageId: string,
  state: MessageTranscriptState,
  text: string | null,
  language: string | null,
): Promise<void> {
  if (!deps.messages) return;
  const message = await deps.messages.findById(env.tenantId, messageId);
  if (!message) return;
  const applied = message.setTranscript({ state, text, language });
  if (!applied.isSuccess) return;
  await deps.messages.update(env.tenantId, message);
  const snap = message.toSnapshot();
  const dto: MessageTranscriptUpdatedDto = {
    conversationId: snap.conversationId,
    messageId,
    transcriptState: state,
    transcriptText: snap.transcriptText ?? null,
  };
  deps.emitter.emitToConversation({
    tenantId: env.tenantId,
    conversationId: snap.conversationId,
    event: ChatSocketEvents.MessageTranscriptUpdated,
    envelope: envelope(env, dto),
  });
}

const envelope = <T>(env: IncomingEnvelope, payload: T) => ({
  eventId: randomUUID(),
  correlationId: env.correlationId ?? '',
  emittedAtUtc: new Date().toISOString(),
  payload,
});

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' && value.length > 0 ? value : undefined;
}

function getNumber(source: Record<string, unknown>, key: string): number | undefined {
  const value = source[key];
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  if (typeof value === 'string') {
    const parsed = Number.parseFloat(value);
    if (Number.isFinite(parsed)) return parsed;
  }
  return undefined;
}
