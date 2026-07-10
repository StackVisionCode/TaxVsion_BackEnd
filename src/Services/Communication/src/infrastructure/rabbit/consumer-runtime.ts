import type { ConsumeMessage } from 'amqplib';
import { getRabbitContext } from './rabbit-connection.js';
import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ProcessedEventStore } from '../../application/ports/processed-event-store.js';
import type { ConsumerHandler, IncomingEnvelope } from '../../application/ports/event-consumer.js';

/**
 * Registry sencillo de consumers por `eventType`. Cada handler valida el shape
 * del payload por su cuenta (los handlers reciben el objeto ya parseado). El
 * runtime se encarga de:
 *   - Ack durable (siempre ack, no requeue en errores no-retriables).
 *   - Idempotencia via ProcessedEventStore (inbox) — cierra CRIT-10 legacy.
 *   - Logs estructurados con eventId, eventType, source.
 *
 * Consumers se registran con `register(eventType, handler)` y arrancan con
 * `start()`. No reinventamos rueda tipo rascal: consumer set fijo por servicio.
 */
export type { ConsumerHandler, IncomingEnvelope };

export class ConsumerRuntime {
  private handlers = new Map<string, ConsumerHandler>();

  constructor(private readonly processedEvents: ProcessedEventStore) {}

  register(eventType: string, handler: ConsumerHandler): void {
    if (this.handlers.has(eventType)) {
      throw new Error(`Duplicate consumer for ${eventType}`);
    }
    this.handlers.set(eventType, handler);
  }

  async start(): Promise<void> {
    const rabbit = getRabbitContext();
    await rabbit.channel.prefetch(20);
    await rabbit.channel.consume(config.rabbitmq.queue, (msg) => {
      if (!msg) return;
      void this.dispatch(msg);
    });
    logger.info({ queue: config.rabbitmq.queue, handlers: this.handlers.size }, 'consumer runtime started');
  }

  private async dispatch(msg: ConsumeMessage): Promise<void> {
    const rabbit = getRabbitContext();
    const raw = msg.content.toString('utf-8');
    let envelope: IncomingEnvelope;
    try {
      const parsed = JSON.parse(raw) as Partial<IncomingEnvelope> & { [k: string]: unknown };
      envelope = this.normalizeEnvelope(parsed);
    } catch (err) {
      logger.warn({ err: (err as Error).message }, 'consumer: unparseable payload; ack to skip');
      rabbit.channel.ack(msg);
      return;
    }

    const handler = this.handlers.get(envelope.eventType);
    if (!handler) {
      rabbit.channel.ack(msg);
      return;
    }

    const fresh = await this.processedEvents.tryMarkProcessed({
      eventId: envelope.eventId,
      source: this.extractSource(envelope.eventType),
      eventType: envelope.eventType,
      tenantId: envelope.tenantId,
    });
    if (!fresh) {
      logger.debug({ eventId: envelope.eventId, eventType: envelope.eventType }, 'inbox: duplicate; skip');
      rabbit.channel.ack(msg);
      return;
    }

    try {
      await handler(envelope);
      rabbit.channel.ack(msg);
    } catch (err) {
      logger.error(
        { err: (err as Error).message, eventId: envelope.eventId, eventType: envelope.eventType },
        'consumer handler failed',
      );
      // Politica: ack + persistir a DLQ manual seria lo correcto; en Fase 4
      // hacemos ack directo para no bucle infinito. Fase 6 agrega DLQ formal.
      rabbit.channel.ack(msg);
    }
  }

  private normalizeEnvelope(raw: Partial<IncomingEnvelope> & Record<string, unknown>): IncomingEnvelope {
    const eventId = typeof raw['eventId'] === 'string' ? (raw['eventId'] as string) : (raw['EventId'] as string);
    const eventType = typeof raw['eventType'] === 'string' ? (raw['eventType'] as string) : (raw['EventType'] as string);
    const tenantId = typeof raw['tenantId'] === 'string' ? (raw['tenantId'] as string) : (raw['TenantId'] as string);
    const occurredOnUtc =
      typeof raw['occurredOnUtc'] === 'string'
        ? (raw['occurredOnUtc'] as string)
        : typeof raw['OccurredOn'] === 'string'
          ? (raw['OccurredOn'] as string)
          : new Date().toISOString();
    const correlationId =
      typeof raw['correlationId'] === 'string'
        ? (raw['correlationId'] as string)
        : typeof raw['CorrelationId'] === 'string'
          ? (raw['CorrelationId'] as string)
          : undefined;
    return {
      eventId,
      eventType,
      tenantId,
      occurredOnUtc,
      ...(correlationId !== undefined ? { correlationId } : {}),
      // El resto del payload queda accesible con la misma forma que llego.
      payload: raw as Readonly<Record<string, unknown>>,
    };
  }

  private extractSource(eventType: string): string {
    // "signature.request.reminder_due.v1" → "signature"
    const idx = eventType.indexOf('.');
    return idx > 0 ? eventType.slice(0, idx) : eventType;
  }
}
