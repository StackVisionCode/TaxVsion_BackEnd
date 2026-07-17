import { randomUUID } from 'node:crypto';
import { getRabbitContext } from './rabbit-connection.js';
import { config } from '../config.js';
import { logger } from '../logger.js';
const KIND_TO_EVENT_TYPE = {
    call: 'communication.call.recording_validation_failed.v1',
    meeting: 'communication.meeting.recording_validation_failed.v1',
};
/**
 * Fase Backend 8 — publica al exchange fanout `taxvision-events`, mismo
 * criterio que `publishTranscriptReady`. Consumer del lado de Communication
 * (transcript-consumers.ts) transiciona la RecordingSession a Failed con el
 * `failureReason` como texto. `NoAudioStream` es el valor tipico (bug #245),
 * pero cualquier string es aceptable.
 */
export function publishRecordingValidationFailed(event) {
    const rabbit = getRabbitContext();
    const eventType = KIND_TO_EVENT_TYPE[event.kind];
    const detectedAtUtc = new Date().toISOString();
    const eventId = randomUUID();
    const body = {
        eventId,
        eventType,
        tenantId: event.tenantId,
        correlationId: event.correlationId,
        occurredOnUtc: detectedAtUtc,
        ...(event.kind === 'call' ? { callId: event.targetId } : { meetingId: event.targetId }),
        recordingFileId: event.recordingFileId,
        failureReason: event.failureReason,
        detectedAtUtc,
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
//# sourceMappingURL=validation-failed-publisher.js.map