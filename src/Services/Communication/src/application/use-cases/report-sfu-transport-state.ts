import { randomUUID } from 'node:crypto';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { SfuConsumerScore, SfuObserver, SfuProducerScore, SfuTransportStateChange, SfuTransportStatsSample } from '../ports/sfu-service.js';
import type { MeetingEventLogRepository } from '../ports/meeting-event-log-repository.js';
import { MeetingEventTypes, type MeetingTransportFailedEvent } from '../../contracts/events/meeting-events.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import { recordMeetingEvent } from './meeting-event-log-writer.js';
import type { MeetingQualityAggregator } from '../services/meeting-quality-aggregator.js';
import { logger } from '../../infrastructure/logger/logger.js';

/**
 * Convierte los cambios de estado de los transports del SFU en hechos del dominio: un fallo DTLS o
 * una desconexion ICE se publica como `transport_failed` para que el MeetingEventLog (F3) y la
 * analitica (F10) lo recojan por eventos. Las transiciones sanas no generan eventos.
 */
export interface ReportSfuTransportStateDeps {
  readonly publisher: IntegrationEventPublisher;
  /** F4: fila `transport.failed` / `sfu.score` en el timeline y agregado para el resumen. */
  readonly meetingEvents?: MeetingEventLogRepository | undefined;
  readonly qualityAggregator?: MeetingQualityAggregator | undefined;
}

/** Umbrales tecnicos del score de mediasoup (0-10): por debajo de BAD hay degradacion real; por encima de OK se recupero. */
export const SFU_SCORE_BAD = 5;
export const SFU_SCORE_OK = 7;

/** mediasoup es ICE-lite: ICE nunca pasa a 'failed'; 'disconnected' es la señal de perdida de red. */
export function isTransportFailure(change: SfuTransportStateChange): boolean {
  if (change.layer === 'dtls') return change.state === 'failed';
  return change.state === 'disconnected';
}

export function buildTransportFailedEvent(change: SfuTransportStateChange, now: Date): MeetingTransportFailedEvent {
  return {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.TransportFailed,
    tenantId: change.tenantId,
    correlationId: change.transportId,
    occurredOnUtc: now.toISOString(),
    meetingId: change.meetingId,
    participantUserId: change.userId,
    direction: change.direction,
    transportId: change.transportId,
    layer: change.layer,
    failedAtUtc: now.toISOString(),
  };
}

export function createSfuTransportStateReporter(deps: ReportSfuTransportStateDeps): SfuObserver {
  // Estado "degradado" por producer/consumer: solo se escribe en el log al cruzar el umbral (sin ruido).
  const degraded = new Set<string>();
  const logEvents = deps.meetingEvents ? { meetingEvents: deps.meetingEvents } : null;

  const trackScore = (key: string, score: number, ctx: { tenantId: string; meetingId: string; userId: string; payload: Record<string, unknown> }) => {
    deps.qualityAggregator?.recordScore(ctx.meetingId, ctx.userId, score);
    const wasDegraded = degraded.has(key);
    if (!wasDegraded && score <= SFU_SCORE_BAD) degraded.add(key);
    else if (wasDegraded && score >= SFU_SCORE_OK) degraded.delete(key);
    else return;
    if (!logEvents || !ctx.tenantId) return;
    void recordMeetingEvent(logEvents, {
      tenantId: ctx.tenantId,
      meetingId: ctx.meetingId,
      type: MeetingEventType.SfuScore,
      actorUserId: ctx.userId,
      payload: { ...ctx.payload, score, degraded: !wasDegraded },
      correlationId: key,
    });
  };

  return {
    onTransportStateChanged(change: SfuTransportStateChange): void {
      if (!isTransportFailure(change)) return;
      // El observador es sincrono (callback de mediasoup); el outbox absorbe el fallo sin tumbar nada.
      void deps.publisher.enqueue(buildTransportFailedEvent(change, new Date())).catch(() => undefined);
      deps.qualityAggregator?.recordTransportFailure(change.meetingId, change.userId);
      if (logEvents) {
        void recordMeetingEvent(logEvents, {
          tenantId: change.tenantId,
          meetingId: change.meetingId,
          type: MeetingEventType.TransportFailed,
          actorUserId: change.userId,
          payload: { direction: change.direction, layer: change.layer, state: change.state, transportId: change.transportId },
          correlationId: change.transportId,
        });
      }
    },
    onProducerScore(score: SfuProducerScore): void {
      // Score del producer = la capa mejor; por capa (rid) queda en el payload cuando cruza el umbral.
      const best = score.scores.reduce((max, s) => Math.max(max, s.score), 0);
      trackScore(`producer:${score.producerId}`, best, {
        tenantId: score.tenantId,
        meetingId: score.meetingId,
        userId: score.userId,
        payload: { side: 'producer', producerId: score.producerId, kind: score.kind, source: score.source, layers: score.scores },
      });
    },
    onConsumerScore(score: SfuConsumerScore): void {
      trackScore(`consumer:${score.consumerId}`, score.score, {
        tenantId: score.tenantId,
        meetingId: score.meetingId,
        userId: score.userId,
        payload: { side: 'consumer', consumerId: score.consumerId, producerId: score.producerId, producerScore: score.producerScore },
      });
    },
    onConsumerLayersChanged(change): void {
      logger.debug({ meetingId: change.meetingId, userId: change.userId, consumerId: change.consumerId, spatial: change.spatialLayer, temporal: change.temporalLayer }, 'sfu consumer layers changed');
    },
    onTransportStats(sample: SfuTransportStatsSample): void {
      logger.debug(
        { meetingId: sample.meetingId, userId: sample.userId, direction: sample.direction, bytesIn: sample.bytesReceived, bytesOut: sample.bytesSent, rtt: sample.rtt, protocol: sample.selectedProtocol },
        'sfu transport stats',
      );
    },
  };
}
