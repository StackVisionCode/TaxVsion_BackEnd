import type { PrismaClient } from '@prisma/client';
import type { IntegrationEvent } from '../../contracts/events/integration-event.js';
import type { IntegrationEventPublisher } from '../../application/ports/integration-event-publisher.js';

/**
 * Outbox transaccional. `enqueue` inserta el evento en OutboxMessage; el worker
 * (RabbitOutboxDrainer, Fase 4) lo drena a taxvision-events. Ventaja: la
 * atomicidad la garantiza SQL Server, no una capa aplicativa fragil.
 *
 * En Fase 1 el drainer no esta implementado — los eventos se acumulan pero
 * no bloquean el ciclo. Se enciende en Fase 4 junto con los consumers de
 * Signature/Customer.
 */
export class PrismaOutboxPublisher implements IntegrationEventPublisher {
  constructor(private readonly prisma: PrismaClient) {}

  async enqueue(event: IntegrationEvent): Promise<void> {
    await this.prisma.outboxMessage.create({
      data: {
        EventId: event.eventId,
        TenantId: event.tenantId,
        EventType: event.eventType,
        Payload: JSON.stringify(event),
        CorrelationId: event.correlationId ?? null,
        OccurredAtUtc: new Date(event.occurredOnUtc),
      },
    });
  }
}
