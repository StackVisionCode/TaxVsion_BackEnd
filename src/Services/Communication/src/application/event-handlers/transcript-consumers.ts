import { randomUUID } from 'node:crypto';
import type { CallRepository } from '../ports/call-repository.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { attachCallTranscript } from '../use-cases/attach-call-transcript.js';
import { attachMeetingTranscript } from '../use-cases/attach-meeting-transcript.js';
import { CallSocketEvents, type CallTranscriptReadyDto } from '../../contracts/socket/call-socket-events.js';
import { MeetingSocketEvents, type MeetingTranscriptReadyDto } from '../../contracts/socket/meeting-socket-events.js';
import { logger } from '../../infrastructure/logger/logger.js';

/**
 * Consumers de `communication.call.transcript_ready.v1` /
 * `.meeting.transcript_ready.v1` — publicados por el worker de transcripts
 * (proceso separado, Fase 6) al terminar de transcribir una grabacion con
 * whisper.cpp. Adjunta el resultado al aggregate y notifica por socket a los
 * participantes que ya salieron del room del call/meeting (sigue existiendo
 * `t:{tenant}:call:{id}` / `t:{tenant}:m:{id}` mientras haya algun listener,
 * pero el caso comun es notificar a quien vuelva a abrir el historial luego).
 */
export function bindTranscriptConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { calls: CallRepository; meetings: MeetingRepository; emitter: RealtimeEmitter },
): void {
  register('communication.call.transcript_ready.v1', async (env) => {
    const callId = getString(env.payload, 'callId');
    const transcriptFileId = getString(env.payload, 'transcriptFileId');
    const language = getString(env.payload, 'language');
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
      language: language ?? null,
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
  });

  register('communication.meeting.transcript_ready.v1', async (env) => {
    const meetingId = getString(env.payload, 'meetingId');
    const transcriptFileId = getString(env.payload, 'transcriptFileId');
    const language = getString(env.payload, 'language');
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
      language: language ?? null,
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
  });
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}
