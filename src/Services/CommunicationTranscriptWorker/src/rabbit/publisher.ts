import { randomUUID } from 'node:crypto';
import { getRabbitContext } from './rabbit-connection.js';
import { config } from '../config.js';
import { logger } from '../logger.js';
import type { TranscriptFailureReason } from '../contracts/events.js';
import { buildRecordingKindLookups, type RecordingKindMapping } from '../contracts/recording-kinds.js';

export interface TranscriptReadyEvent {
  readonly kind: string;
  readonly tenantId: string;
  readonly correlationId: string | undefined;
  readonly targetId: string;
  readonly recordingFileId: string;
  readonly transcriptFileId: string;
  readonly detectedLanguage: string | null;
  /** Fase Transcript 5 — duracion del audio en segundos, derivada del ultimo timestamp de whisper.cpp. */
  readonly durationSeconds: number;
  /** Fase Transcript 5 — conteo de palabras del transcript ya limpio (sin timestamps). */
  readonly wordCount: number;
}

const { byKind } = buildRecordingKindLookups(config.recordingKinds);

function mappingForKind(kind: string): RecordingKindMapping {
  const mapping = byKind.get(kind);
  if (!mapping) throw new Error(`Unknown recording kind: ${kind} (ver config.recordingKinds)`);
  return mapping;
}

/**
 * Publica al mismo exchange fanout `taxvision-events` que usa Communication.
 * El `eventType` va tanto en el JSON body como en el header AMQP `type`,
 * replicando la convencion propia de `PrismaOutboxPublisher`/`outbox-drainer`
 * — asi `normalizeEnvelope` del lado de Communication lo reconoce por el
 * campo del body sin necesitar una entrada en `CLR_TYPE_TO_EVENT_TYPE`
 * (ese mapeo es solo para productores .NET/Wolverine que no ponen eventType
 * en el body).
 */
export function publishTranscriptReady(event: TranscriptReadyEvent): void {
  const rabbit = getRabbitContext();
  const mapping = mappingForKind(event.kind);
  const eventType = mapping.transcriptReadyEventType;
  const readyAtUtc = new Date().toISOString();
  const eventId = randomUUID();
  const body = {
    eventId,
    eventType,
    tenantId: event.tenantId,
    correlationId: event.correlationId,
    occurredOnUtc: readyAtUtc,
    [mapping.targetIdField]: event.targetId,
    recordingFileId: event.recordingFileId,
    transcriptFileId: event.transcriptFileId,
    detectedLanguage: event.detectedLanguage,
    durationSeconds: event.durationSeconds,
    wordCount: event.wordCount,
    readyAtUtc,
  };

  const ok = rabbit.channel.publish(config.rabbitmq.exchange, '', Buffer.from(JSON.stringify(body), 'utf-8'), {
    contentType: 'application/json',
    persistent: true,
    messageId: eventId,
    type: eventType,
    timestamp: Math.floor(Date.now() / 1000),
  });
  if (!ok) {
    logger.warn({ eventType, targetId: event.targetId }, 'publish backpressure — channel buffer full');
  }
}

export interface TranscriptFailedEvent {
  readonly kind: string;
  readonly tenantId: string;
  readonly correlationId: string | undefined;
  readonly targetId: string;
  readonly recordingFileId: string;
  readonly failureReason: TranscriptFailureReason;
  readonly errorMessage: string;
}

/**
 * Fase Transcript 1 (contrato) + Fase Transcript 2/3 (wireado en pipeline.ts,
 * ver docblock ahi). Mismo criterio de publish que `publishTranscriptReady`:
 * fanout `taxvision-events`, `eventType` en el body Y en el header AMQP `type`.
 */
export function publishTranscriptFailed(event: TranscriptFailedEvent): void {
  const rabbit = getRabbitContext();
  const mapping = mappingForKind(event.kind);
  const eventType = mapping.transcriptFailedEventType;
  const occurredAtUtc = new Date().toISOString();
  const eventId = randomUUID();
  const body = {
    eventId,
    eventType,
    tenantId: event.tenantId,
    correlationId: event.correlationId,
    occurredOnUtc: occurredAtUtc,
    [mapping.targetIdField]: event.targetId,
    recordingFileId: event.recordingFileId,
    failureReason: event.failureReason,
    errorMessage: event.errorMessage,
    occurredAtUtc,
  };

  const ok = rabbit.channel.publish(config.rabbitmq.exchange, '', Buffer.from(JSON.stringify(body), 'utf-8'), {
    contentType: 'application/json',
    persistent: true,
    messageId: eventId,
    type: eventType,
    timestamp: Math.floor(Date.now() / 1000),
  });
  if (!ok) {
    logger.warn({ eventType, targetId: event.targetId }, 'publish backpressure — channel buffer full');
  }
}
