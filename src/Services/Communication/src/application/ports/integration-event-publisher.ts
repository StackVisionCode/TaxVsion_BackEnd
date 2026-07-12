import type { IntegrationEvent } from '../../contracts/events/integration-event.js';

/**
 * Puerto para publicar integration events al bus. La implementacion usa el
 * outbox transaccional Prisma + un worker que drena la tabla OutboxMessage a
 * RabbitMQ. Cierra CRIT-8 legacy (guarda + emit sin transaccion): aqui,
 * `enqueue` participa de la MISMA transaccion Prisma que el aggregate.
 */
export interface IntegrationEventPublisher {
  /**
   * Encola un evento en el outbox. Debe llamarse dentro de la misma
   * transaccion Prisma que la mutacion del aggregate.
   */
  enqueue(event: IntegrationEvent): Promise<void>;
}
