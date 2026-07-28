import { z } from 'zod';
const recordingKindMappingSchema = z.object({
    kind: z.string().min(1),
    targetIdField: z.string().min(1),
    triggerEventTypes: z.array(z.string().min(1)).min(1),
    transcriptReadyEventType: z.string().min(1),
    transcriptFailedEventType: z.string().min(1),
});
export const recordingKindsSchema = z.array(recordingKindMappingSchema).min(1);
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
        triggerEventTypes: [
            'communication.call.recording_ready.v1',
            'communication.call.recording_processing_started.v1',
        ],
        transcriptReadyEventType: 'communication.call.transcript_ready.v1',
        transcriptFailedEventType: 'communication.call.transcript_failed.v1',
    },
    {
        kind: 'meeting',
        targetIdField: 'meetingId',
        triggerEventTypes: [
            'communication.meeting.recording_ready.v1',
            'communication.meeting.recording_processing_started.v1',
        ],
        transcriptReadyEventType: 'communication.meeting.transcript_ready.v1',
        transcriptFailedEventType: 'communication.meeting.transcript_failed.v1',
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