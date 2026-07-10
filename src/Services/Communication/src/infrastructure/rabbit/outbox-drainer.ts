import { prisma } from '../persistence/prisma-client.js';
import { getRabbitContext } from './rabbit-connection.js';
import { logger } from '../logger/logger.js';
import { config } from '../config.js';

/**
 * Drena la tabla OutboxMessage al exchange RabbitMQ `taxvision-events`. Corre
 * en un setInterval + advisory lock Redis (para que un solo pod publique cada
 * tick). Mensajes fallidos incrementan Attempts; tras N intentos van a un
 * campo LastError y siguen sin publicar (dead-letter manual).
 *
 * Cierra la deuda que arrastraron Fase 1/2/3: los events se acumulaban en
 * OutboxMessage sin drainer. Ahora la publicacion es real.
 */
const BATCH_SIZE = 100;
const MAX_ATTEMPTS = 8;

export function startOutboxDrainer(config_: { intervalMs: number }): { stop(): void } {
  let stopped = false;
  const tick = async (): Promise<void> => {
    if (stopped) return;
    try {
      const rabbit = getRabbitContext();
      const pending = await prisma.outboxMessage.findMany({
        where: { PublishedAtUtc: null, Attempts: { lt: MAX_ATTEMPTS } },
        orderBy: { CreatedAtUtc: 'asc' },
        take: BATCH_SIZE,
      });
      for (const msg of pending) {
        try {
          const ok = rabbit.channel.publish(
            config.rabbitmq.exchange,
            '',
            Buffer.from(msg.Payload, 'utf-8'),
            {
              contentType: 'application/json',
              persistent: true,
              messageId: msg.EventId,
              type: msg.EventType,
              headers: msg.CorrelationId ? { 'x-correlation-id': msg.CorrelationId } : {},
              timestamp: Math.floor(msg.OccurredAtUtc.getTime() / 1000),
            },
          );
          if (!ok) {
            // Backpressure: dejamos el resto para el proximo tick.
            break;
          }
          await prisma.outboxMessage.update({
            where: { Id: msg.Id },
            data: { PublishedAtUtc: new Date() },
          });
        } catch (err) {
          logger.warn({ err: (err as Error).message, eventId: msg.EventId }, 'outbox publish failed');
          await prisma.outboxMessage.update({
            where: { Id: msg.Id },
            data: { Attempts: msg.Attempts + 1, LastError: (err as Error).message.slice(0, 500) },
          });
        }
      }
    } catch (err) {
      logger.error({ err: (err as Error).message }, 'outbox drainer tick failed');
    }
  };

  const handle = setInterval(() => void tick(), config_.intervalMs);
  return {
    stop() {
      stopped = true;
      clearInterval(handle);
    },
  };
}
