import { getRabbitContext } from './rabbit-connection.js';
import { config } from '../config.js';
import { logger } from '../logger.js';
const EVENT_TYPE_TO_KIND = {
    'communication.call.recording_ready.v1': 'call',
    'communication.meeting.recording_ready.v1': 'meeting',
};
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
export function startConsumer(handler, deps) {
    const rabbit = getRabbitContext();
    void rabbit.channel.prefetch(config.concurrency);
    void rabbit.channel.consume(config.rabbitmq.queue, (msg) => {
        if (!msg)
            return;
        void dispatch(msg, handler, deps);
    });
    logger.info({ queue: config.rabbitmq.queue }, 'transcript worker consumer started');
}
async function dispatch(msg, handler, deps) {
    const rabbit = getRabbitContext();
    let parsed;
    try {
        parsed = JSON.parse(msg.content.toString('utf-8'));
    }
    catch (err) {
        logger.warn({ err: err.message }, 'unparseable message; ack to skip');
        rabbit.channel.ack(msg);
        return;
    }
    const eventType = str(parsed['eventType']);
    const kind = eventType ? EVENT_TYPE_TO_KIND[eventType] : undefined;
    if (!kind) {
        // No es un recording_ready (chat, presence, etc.) — no nos interesa.
        rabbit.channel.ack(msg);
        return;
    }
    const event = toRecordingReadyEvent(kind, parsed);
    if (!event) {
        logger.warn({ eventType }, 'recording_ready payload missing required fields; ack to skip');
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
    }
    catch (err) {
        logger.error({ err: err.message, eventId: event.eventId, targetId: event.targetId }, 'transcript pipeline failed — dead-lettering');
        await deps.inbox
            .unmark(event.eventId)
            .catch((unmarkErr) => logger.warn({ err: unmarkErr.message }, 'inbox unmark after failure failed'));
        rabbit.channel.nack(msg, false, false);
    }
}
function toRecordingReadyEvent(kind, raw) {
    const eventId = str(raw['eventId']);
    const tenantId = str(raw['tenantId']);
    const recordingFileId = str(raw['recordingFileId']);
    const targetId = kind === 'call' ? str(raw['callId']) : str(raw['meetingId']);
    if (!eventId || !tenantId || !recordingFileId || !targetId)
        return undefined;
    return { kind, eventId, tenantId, correlationId: str(raw['correlationId']), targetId, recordingFileId };
}
function str(value) {
    return typeof value === 'string' ? value : undefined;
}
//# sourceMappingURL=consumer.js.map