import type { ConsumeMessage } from 'amqplib';
import { getRabbitContext } from './rabbit-connection.js';
import { config } from '../config.js';
import { logger } from '../logger.js';
import type { TranscriptInbox } from '../redis/transcript-inbox.js';
import {
  buildRecordingKindLookups,
  type RecordingKindMapping,
  type TranscriptOutputMode,
} from '../contracts/recording-kinds.js';

/** Pista de audio por participante (contrato 9.3, T-28). */
export interface AudioTrackRef {
  readonly fileId: string;
  readonly userId: string;
  readonly displayName: string | null;
  readonly startOffsetMs: number;
}

export type PassthroughValue = string | number | boolean | null;

export interface RecordingReadyEvent {
  readonly kind: string;
  readonly eventId: string;
  readonly tenantId: string;
  readonly correlationId: string | undefined;
  readonly targetId: string;
  readonly recordingFileId: string;
  readonly output: TranscriptOutputMode;
  /** Campos del trigger que se devuelven tal cual (RecordingKindMapping.passthroughFields). */
  readonly passthrough: Readonly<Record<string, PassthroughValue>>;
  readonly audioTracks?: readonly AudioTrackRef[] | undefined;
  /** 'auto' o codigo ISO; manda sobre TRANSCRIPT_WORKER_WHISPER_LANGUAGE. */
  readonly language?: string | undefined;
  readonly prompt?: string | null | undefined;
  readonly maxDurationSeconds?: number | undefined;
  readonly timeoutFactorPct?: number | undefined;
}

/**
 * Dos triggers distintos segun el path de grabacion (ver docblock de
 * `dispatch`/dispatch.ts arriba y pipeline.ts):
 *   - `recording_ready.v1`: path LEGACY (sin RecordingSession/consent) —
 *     attach-{call,meeting}-recording.ts lo publica directo, es el unico
 *     evento de todo el ciclo de vida de esa grabacion.
 *   - `recording_processing_started.v1`: path CON RecordingSession/consent.
 *     Investigado en Fase Transcript 3: el flujo con consentimiento NUNCA
 *     publicaba un evento pre-transcripcion que disparara este worker — el
 *     unico `recording_ready.v1` de ese path se publicaba DESPUES de que el
 *     worker ya habia transcrito (dentro del handler de `transcript_ready.v1`
 *     en Communication), lo cual es circular como trigger inicial. Se agrego
 *     este mapeo para que el path con consentimiento arranque la
 *     transcripcion en el momento correcto (Stopping->Processing, cuando
 *     `recordingFileId` ya esta disponible) — sin tocar el shape del evento
 *     ni el codigo de Communication que ya lo publicaba con ese proposito
 *     (solo que antes nada lo escuchaba). Communication ya NO re-publica
 *     `recording_ready.v1` despues de transcribir (removido junto con este
 *     fix, era redundante y hubiera causado un reproceso duplicado aca).
 *
 * Generico desde el punto de vista de este archivo: el mapeo evento->kind y
 * kind->campo-de-id vive en `config.recordingKinds` (contracts/recording-kinds.ts),
 * no hardcodeado aca. Por default es exactamente el mapeo de arriba
 * (Communication); otro consumidor de este worker configura su propio mapeo
 * via TRANSCRIPT_WORKER_RECORDING_KINDS sin tocar este archivo.
 */
const { eventTypeToKind } = buildRecordingKindLookups(config.recordingKinds);

export interface ConsumerHandle {
  /**
   * Fase Transcript 4 — graceful drain: cancela el consumer tag (RabbitMQ
   * deja de entregar mensajes NUEVOS de inmediato) y espera hasta
   * `drainTimeoutMs` a que los `dispatch()` YA en curso (contados via
   * `inFlightCount`) terminen — un job de transcripcion puede tardar minutos
   * (ffmpeg+whisper+retries de red), asi que matar el proceso a mitad de
   * camino corrompe el estado (temp dir a medio limpiar, RecordingSession
   * que nunca se entera de si el trabajo termino o no). Si el timeout se
   * cumple igual, el proceso sigue con el shutdown de todos modos — ese
   * mensaje vuelve a entregarse cuando el worker reconecte.
   */
  stop(drainTimeoutMs?: number): Promise<void>;
}

let inFlightCount = 0;

/**
 * Inbox propio via Redis (`TranscriptInbox`) — cierra el gap de idempotencia
 * consumer-side: sin el, una redelivery de RabbitMQ reprocesa una grabacion
 * ya transcrita (minutos de CPU en ffmpeg/whisper.cpp) antes de que
 * `attachCallTranscript`/`attachMeetingTranscript` del lado de Communication
 * la rechace por duplicada. El marcado ocurre ANTES del pipeline (evita la
 * ventana de carrera entre dos deliveries concurrentes del mismo eventId) y
 * se revierte solo si el pipeline falla, para que un retry legitimo no
 * quede bloqueado como falso duplicado.
 */
export async function startConsumer(
  handler: (event: RecordingReadyEvent) => Promise<void>,
  deps: { inbox: TranscriptInbox },
): Promise<ConsumerHandle> {
  const rabbit = getRabbitContext();
  await rabbit.channel.prefetch(config.concurrency);
  const { consumerTag } = await rabbit.channel.consume(config.rabbitmq.queue, (msg) => {
    if (!msg) return;
    void dispatch(msg, handler, deps);
  });
  logger.info({ queue: config.rabbitmq.queue }, 'transcript worker consumer started');

  return {
    async stop(drainTimeoutMs = 60_000): Promise<void> {
      try {
        await rabbit.channel.cancel(consumerTag);
      } catch (err) {
        logger.warn({ err: (err as Error).message }, 'consumer.stop: channel.cancel failed');
      }
      const deadline = Date.now() + drainTimeoutMs;
      while (inFlightCount > 0 && Date.now() < deadline) {
        await new Promise((resolve) => setTimeout(resolve, 200));
      }
      if (inFlightCount > 0) {
        logger.warn(
          { inFlight: inFlightCount, drainTimeoutMs },
          'consumer.stop: drain timeout exceeded, job(s) still in flight',
        );
      } else {
        logger.info('consumer.stop: drained cleanly');
      }
    },
  };
}

async function dispatch(
  msg: ConsumeMessage,
  handler: (event: RecordingReadyEvent) => Promise<void>,
  deps: { inbox: TranscriptInbox },
): Promise<void> {
  inFlightCount += 1;
  try {
    await dispatchInner(msg, handler, deps);
  } finally {
    inFlightCount -= 1;
  }
}

async function dispatchInner(
  msg: ConsumeMessage,
  handler: (event: RecordingReadyEvent) => Promise<void>,
  deps: { inbox: TranscriptInbox },
): Promise<void> {
  const rabbit = getRabbitContext();
  let parsed: Record<string, unknown>;
  try {
    parsed = JSON.parse(msg.content.toString('utf-8')) as Record<string, unknown>;
  } catch (err) {
    logger.warn({ err: (err as Error).message }, 'unparseable message; ack to skip');
    rabbit.channel.ack(msg);
    return;
  }

  const eventType = str(parsed['eventType']);
  const mapping = eventType ? eventTypeToKind.get(eventType) : undefined;
  if (!mapping) {
    // No es un trigger de transcripcion (chat, presence, etc.) — no nos interesa.
    rabbit.channel.ack(msg);
    return;
  }

  const event = toRecordingReadyEvent(mapping, parsed);
  if (!event) {
    logger.warn({ eventType }, 'trigger event missing required fields; ack to skip');
    rabbit.channel.ack(msg);
    return;
  }

  const fresh = await deps.inbox.tryMarkProcessed(event.eventId);
  if (!fresh) {
    logger.info({ eventId: event.eventId, targetId: event.targetId }, 'inbox: duplicate; skip');
    rabbit.channel.ack(msg);
    return;
  }

  try {
    await handler(event);
    rabbit.channel.ack(msg);
  } catch (err) {
    // Fase Transcript 2 — este catch generico NO cambio: sigue haciendo
    // exactamente log + inbox.unmark + nack a DLQ, sin reintentos in-worker
    // (la DLQ sigue siendo la unica superficie de retry). Lo que si cambio
    // es que, para la mayoria de los errores que llegan hasta aca,
    // `processRecordingReady` (pipeline.ts) YA publico un evento
    // `TranscriptFailed` con el reason especifico del stage que fallo
    // (DownloadFailed/FfmpegError/WhisperError/UploadFailed/Timeout) antes de
    // relanzar el error — este catch ya no es la unica senal de "algo fallo",
    // solo la que decide el destino del mensaje AMQP.
    logger.error(
      { err: (err as Error).message, eventId: event.eventId, targetId: event.targetId },
      'transcript pipeline failed — dead-lettering',
    );
    await deps.inbox
      .unmark(event.eventId)
      .catch((unmarkErr: unknown) =>
        logger.warn({ err: (unmarkErr as Error).message }, 'inbox unmark after failure failed'),
      );
    rabbit.channel.nack(msg, false, false);
  }
}

export function toRecordingReadyEvent(
  mapping: RecordingKindMapping,
  raw: Record<string, unknown>,
): RecordingReadyEvent | undefined {
  const eventId = str(raw['eventId']);
  const tenantId = str(raw['tenantId']);
  const recordingFileId = str(raw[mapping.recordingFileIdField]);
  const targetId = str(raw[mapping.targetIdField]);
  if (!eventId || !tenantId || !recordingFileId || !targetId) return undefined;

  const passthrough: Record<string, PassthroughValue> = {};
  for (const field of mapping.passthroughFields) {
    const value = raw[field];
    if (value === null || ['string', 'number', 'boolean'].includes(typeof value)) {
      passthrough[field] = value as PassthroughValue;
    }
  }

  const audioTracks = parseAudioTracks(raw['audioTracks']);
  const prompt = raw['prompt'];
  return {
    kind: mapping.kind,
    eventId,
    tenantId,
    correlationId: str(raw['correlationId']),
    targetId,
    recordingFileId,
    output: mapping.output,
    passthrough,
    audioTracks: audioTracks.length > 0 ? audioTracks : undefined,
    language: str(raw['language'])?.trim() || undefined,
    prompt: typeof prompt === 'string' ? prompt : prompt === null ? null : undefined,
    maxDurationSeconds: positiveNumber(raw['maxDurationSeconds']),
    timeoutFactorPct: positiveNumber(raw['timeoutFactorPct']),
  };
}

/** Pistas invalidas (sin fileId/userId) se descartan; el resto sigue. */
export function parseAudioTracks(value: unknown): AudioTrackRef[] {
  if (!Array.isArray(value)) return [];
  const tracks: AudioTrackRef[] = [];
  for (const item of value) {
    if (!item || typeof item !== 'object') continue;
    const track = item as Record<string, unknown>;
    const fileId = str(track['fileId']);
    const userId = str(track['userId']);
    if (!fileId || !userId) continue;
    const offset = Number(track['startOffsetMs']);
    tracks.push({
      fileId,
      userId,
      displayName: str(track['displayName'])?.trim() || null,
      startOffsetMs: Number.isFinite(offset) && offset > 0 ? Math.round(offset) : 0,
    });
  }
  return tracks;
}

function str(value: unknown): string | undefined {
  return typeof value === 'string' ? value : undefined;
}

function positiveNumber(value: unknown): number | undefined {
  const n = typeof value === 'number' ? value : typeof value === 'string' && value.trim() ? Number(value) : NaN;
  return Number.isFinite(n) && n > 0 ? n : undefined;
}
