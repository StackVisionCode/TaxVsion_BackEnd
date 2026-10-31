import { z } from 'zod';

/**
 * Este worker es un pipeline generico de transcripcion (CloudStorage -> ffmpeg
 * -> whisper.cpp -> CloudStorage -> evento de resultado). Su UNICA
 * responsabilidad es esa transcodificacion+transcripcion; no sabe nada de
 * "llamadas" ni "reuniones" en si mismo (ver pipeline.ts). Lo unico que
 * varia por dominio es el *contrato de mensajeria*: que tipos de evento RabbitMQ
 * disparan una transcripcion, en que campo del payload viene el id del
 * "dueño" de la grabacion, y con que tipos de evento se publica el resultado.
 *
 * `RecordingKindMapping` es exactamente ese contrato, parametrizado. Cualquier
 * microservicio (no solo Communication) puede reusar este worker tal cual
 * desplegando su propia instancia y configurando `TRANSCRIPT_WORKER_RECORDING_KINDS`
 * (ver config.ts) con sus propios tipos de evento/campo de id — sin tocar
 * una linea de codigo. Si esa variable no esta seteada, se usa
 * `COMMUNICATION_RECORDING_KINDS` de abajo, que reproduce EXACTAMENTE el
 * comportamiento que este worker tuvo desde Fase Transcript 1: call/meeting
 * hardcodeados. Communication no requiere ningun cambio de configuracion.
 */
export interface RecordingKindMapping {
  /** Identificador logico del tipo de grabacion (antes un union type fijo 'call'|'meeting', ahora cualquier string). */
  readonly kind: string;
  /** Nombre del campo en el payload del evento trigger que trae el id del "dueño" (ej. 'callId', 'meetingId', 'episodeId'). */
  readonly targetIdField: string;
  /** Tipos de evento RabbitMQ (campo `eventType` del body) que disparan una transcripcion para este kind. */
  readonly triggerEventTypes: readonly string[];
  /** Tipo de evento publicado cuando la transcripcion termina OK. */
  readonly transcriptReadyEventType: string;
  /** Tipo de evento publicado cuando cualquier stage del pipeline falla. */
  readonly transcriptFailedEventType: string;
  /** Campo del trigger con el fileId del audio (default `recordingFileId`; notas de voz: `fileId`). */
  readonly recordingFileIdField: string;
  /**
   * `file`: sube TXT + JSON v2 a CloudStorage y publica sus fileIds.
   * `inline`: sin subida; el evento ready lleva `transcriptText` (clips cortos, D-27).
   */
  readonly output: TranscriptOutputMode;
  /** Campos del trigger copiados tal cual a ready/failed (ej. `conversationId`). */
  readonly passthroughFields: readonly string[];
}

export type TranscriptOutputMode = 'file' | 'inline';

const recordingKindMappingSchema = z.object({
  kind: z.string().min(1),
  targetIdField: z.string().min(1),
  triggerEventTypes: z.array(z.string().min(1)).min(1),
  transcriptReadyEventType: z.string().min(1),
  transcriptFailedEventType: z.string().min(1),
  recordingFileIdField: z.string().min(1).default('recordingFileId'),
  output: z.enum(['file', 'inline']).default('file'),
  passthroughFields: z.array(z.string().min(1)).default([]),
});

export const recordingKindsSchema = z.array(recordingKindMappingSchema).min(1);

/** Parsea el JSON de TRANSCRIPT_WORKER_RECORDING_KINDS; vacio = default de Communication. */
export function parseRecordingKinds(raw: string): readonly RecordingKindMapping[] {
  if (!raw.trim()) return COMMUNICATION_RECORDING_KINDS;
  return recordingKindsSchema.parse(JSON.parse(raw));
}

/**
 * Default — reproduce 1:1 el mapeo que estaba hardcodeado en
 * rabbit/consumer.ts (EVENT_TYPE_TO_KIND) y rabbit/publisher.ts
 * (KIND_TO_EVENT_TYPE) antes de esta generalizacion. Se sigue usando tal
 * cual mientras TRANSCRIPT_WORKER_RECORDING_KINDS no este configurada.
 */
export const COMMUNICATION_RECORDING_KINDS: readonly RecordingKindMapping[] = [
  {
    kind: 'call',
    targetIdField: 'callId',
    // F7 (T-34): Ready = archivo disponible; el transcript se pide SOLO con processing_started (trae las
    // pistas por participante). Escuchar tambien recording_ready lo transcribia dos veces y pisaba el bueno.
    triggerEventTypes: ['communication.call.recording_processing_started.v1'],
    transcriptReadyEventType: 'communication.call.transcript_ready.v1',
    transcriptFailedEventType: 'communication.call.transcript_failed.v1',
    recordingFileIdField: 'recordingFileId',
    output: 'file',
    passthroughFields: [],
  },
  {
    kind: 'meeting',
    targetIdField: 'meetingId',
    // F7 (T-34): Ready = archivo disponible; el transcript se pide SOLO con processing_started (trae las
    // pistas por participante). Escuchar tambien recording_ready lo transcribia dos veces y pisaba el bueno.
    triggerEventTypes: ['communication.meeting.recording_processing_started.v1'],
    transcriptReadyEventType: 'communication.meeting.transcript_ready.v1',
    transcriptFailedEventType: 'communication.meeting.transcript_failed.v1',
    recordingFileIdField: 'recordingFileId',
    output: 'file',
    passthroughFields: [],
  },
  // D-27 — notas de voz: salida inline (texto en el mensaje, sin .txt).
  {
    kind: 'voice-note',
    targetIdField: 'messageId',
    triggerEventTypes: ['communication.chat.voice_note_transcription_requested.v1'],
    transcriptReadyEventType: 'communication.chat.voice_note_transcript_ready.v1',
    transcriptFailedEventType: 'communication.chat.voice_note_transcript_failed.v1',
    recordingFileIdField: 'fileId',
    output: 'inline',
    passthroughFields: ['conversationId'],
  },
];

export interface RecordingKindLookups {
  readonly eventTypeToKind: ReadonlyMap<string, RecordingKindMapping>;
  readonly byKind: ReadonlyMap<string, RecordingKindMapping>;
}

export function buildRecordingKindLookups(mappings: readonly RecordingKindMapping[]): RecordingKindLookups {
  const eventTypeToKind = new Map<string, RecordingKindMapping>();
  const byKind = new Map<string, RecordingKindMapping>();
  for (const mapping of mappings) {
    byKind.set(mapping.kind, mapping);
    for (const eventType of mapping.triggerEventTypes) {
      eventTypeToKind.set(eventType, mapping);
    }
  }
  return { eventTypeToKind, byKind };
}
