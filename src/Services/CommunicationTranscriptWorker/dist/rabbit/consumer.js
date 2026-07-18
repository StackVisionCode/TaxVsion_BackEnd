import { getRabbitContext } from './rabbit-connection.js';
import { config } from '../config.js';
import { logger } from '../logger.js';
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
 */
const EVENT_TYPE_TO_KIND = {
    'communication.call.recording_ready.v1': 'call',
    'communication.meeting.recording_ready.v1': 'meeting',
    'communication.call.recording_processing_started.v1': 'call',
    'communication.meeting.recording_processing_started.v1': 'meeting',
};
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
export async function startConsumer(handler, deps) {
    const rabbit = getRabbitContext();
    await rabbit.channel.prefetch(config.concurrency);
    const { consumerTag } = await rabbit.channel.consume(config.rabbitmq.queue, (msg) => {
        if (!msg)
            return;
        void dispatch(msg, handler, deps);
    });
    logger.info({ queue: config.rabbitmq.queue }, 'transcript worker consumer started');
    return {
        async stop(drainTimeoutMs = 60_000) {
            try {
                await rabbit.channel.cancel(consumerTag);
            }
            catch (err) {
                logger.warn({ err: err.message }, 'consumer.stop: channel.cancel failed');
            }
            const deadline = Date.now() + drainTimeoutMs;
            while (inFlightCount > 0 && Date.now() < deadline) {
                await new Promise((resolve) => setTimeout(resolve, 200));
            }
            if (inFlightCount > 0) {
                logger.warn({ inFlight: inFlightCount, drainTimeoutMs }, 'consumer.stop: drain timeout exceeded, job(s) still in flight');
            }
            else {
                logger.info('consumer.stop: drained cleanly');
            }
        },
    };
}
async function dispatch(msg, handler, deps) {
    inFlightCount += 1;
    try {
        await dispatchInner(msg, handler, deps);
    }
    finally {
        inFlightCount -= 1;
    }
}
async function dispatchInner(msg, handler, deps) {
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
        // No es un trigger de transcripcion (chat, presence, etc.) — no nos interesa.
        rabbit.channel.ack(msg);
        return;
    }
    const event = toRecordingReadyEvent(kind, parsed);
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
    }
    catch (err) {
        // Fase Transcript 2 — este catch generico NO cambio: sigue haciendo
        // exactamente log + inbox.unmark + nack a DLQ, sin reintentos in-worker
        // (la DLQ sigue siendo la unica superficie de retry). Lo que si cambio
        // es que, para la mayoria de los errores que llegan hasta aca,
        // `processRecordingReady` (pipeline.ts) YA publico un evento
        // `TranscriptFailed` con el reason especifico del stage que fallo
        // (DownloadFailed/FfmpegError/WhisperError/UploadFailed/Timeout) antes de
        // relanzar el error — este catch ya no es la unica senal de "algo fallo",
        // solo la que decide el destino del mensaje AMQP.
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