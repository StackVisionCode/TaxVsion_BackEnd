import { randomUUID } from 'node:crypto';
import { getRabbitContext } from './rabbit-connection.js';
import { config } from '../config.js';
import { logger } from '../logger.js';
import type { TranscriptFailureReason } from '../contracts/events.js';
import { buildRecordingKindLookups, type RecordingKindMapping } from '../contracts/recording-kinds.js';
import type { PassthroughValue } from './consumer.js';

/** Salida `file`: TXT + JSON v2 ya subidos a CloudStorage (contrato 9.4). */
export interface FileTranscriptOutput {
  readonly type: 'file';
  readonly transcriptFileId: string;
  readonly transcriptJsonFileId: string | null;
  readonly segmentCount: number;
  readonly speakerCount: number;
}

/** Salida `inline`: el texto viaja en el evento (notas de voz, D-27). */
export interface InlineTranscriptOutput {
  readonly type: 'inline';
  readonly transcriptText: string;
}

export interface TranscriptReadyEvent {
  readonly kind: string;
  readonly tenantId: string;
  readonly correlationId: string | undefined;
  readonly targetId: string;
  readonly recordingFileId: string;
  readonly passthrough?: Readonly<Record<string, PassthroughValue>> | undefined;
  readonly detectedLanguage: string | null;
  /** Duracion del audio en segundos (ffprobe; con pistas, fin de la ultima). */
  readonly durationSeconds: number;
  readonly wordCount: number;
  readonly model: string;
  readonly vad: boolean;
  readonly realtimeFactor: number | null;
  readonly wordsPerSecond: number | null;
  readonly output: FileTranscriptOutput | InlineTranscriptOutput;
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
  const outputFields =
    event.output.type === 'file'
      ? {
          transcriptFileId: event.output.transcriptFileId,
          transcriptJsonFileId: event.output.transcriptJsonFileId,
          segmentCount: event.output.segmentCount,
          speakerCount: event.output.speakerCount,
        }
      : { transcriptText: event.output.transcriptText };
  const body = {
    ...event.passthrough,
    eventId,
    eventType,
    tenantId: event.tenantId,
    correlationId: event.correlationId,
    occurredOnUtc: readyAtUtc,
    [mapping.targetIdField]: event.targetId,
    [mapping.recordingFileIdField]: event.recordingFileId,
    ...outputFields,
    detectedLanguage: event.detectedLanguage,
    durationSeconds: event.durationSeconds,
    wordCount: event.wordCount,
    model: event.model,
    vad: event.vad,
    realtimeFactor: event.realtimeFactor,
    wordsPerSecond: event.wordsPerSecond,
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
  readonly passthrough?: Readonly<Record<string, PassthroughValue>> | undefined;
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
    ...event.passthrough,
    eventId,
    eventType,
    tenantId: event.tenantId,
    correlationId: event.correlationId,
    occurredOnUtc: occurredAtUtc,
    [mapping.targetIdField]: event.targetId,
    [mapping.recordingFileIdField]: event.recordingFileId,
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
