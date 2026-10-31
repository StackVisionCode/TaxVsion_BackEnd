import { z } from 'zod';
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
export function parseRecordingKinds(raw) {
    if (!raw.trim())
        return COMMUNICATION_RECORDING_KINDS;
    return recordingKindsSchema.parse(JSON.parse(raw));
}
/**
 * Default — reproduce 1:1 el mapeo que estaba hardcodeado en
 * rabbit/consumer.ts (EVENT_TYPE_TO_KIND) y rabbit/publisher.ts
 * (KIND_TO_EVENT_TYPE) antes de esta generalizacion. Se sigue usando tal
 * cual mientras TRANSCRIPT_WORKER_RECORDING_KINDS no este configurada.
 */
export const COMMUNICATION_RECORDING_KINDS = [
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
export function buildRecordingKindLookups(mappings) {
    const eventTypeToKind = new Map();
    const byKind = new Map();
    for (const mapping of mappings) {
        byKind.set(mapping.kind, mapping);
        for (const eventType of mapping.triggerEventTypes) {
            eventTypeToKind.set(eventType, mapping);
        }
    }
    return { eventTypeToKind, byKind };
}
//# sourceMappingURL=recording-kinds.js.map