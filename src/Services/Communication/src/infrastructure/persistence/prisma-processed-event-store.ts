import type { PrismaClient } from '@prisma/client';
import { Prisma } from '@prisma/client';
import type { ProcessedEventStore } from '../../application/ports/processed-event-store.js';

/**
 * Inbox durable — INSERT ... ON DUPLICATE marca fresh vs duplicate. Cierra el
 * gap de idempotencia consumer-side (RabbitMQ redeliveries no ejecutan el
 * handler dos veces).
 */
export class PrismaProcessedEventStore implements ProcessedEventStore {
  constructor(private readonly prisma: PrismaClient) {}

  async tryMarkProcessed(input: {
    eventId: string;
    source: string;
    eventType: string;
    tenantId?: string | null;
  }): Promise<boolean> {
    try {
      await this.prisma.processedEvent.create({
        data: {
          EventId: input.eventId,
          Source: input.source,
          EventType: input.eventType,
          TenantId: input.tenantId ?? null,
        },
      });
      return true;
    } catch (err) {
      if (err instanceof Prisma.PrismaClientKnownRequestError && err.code === 'P2002') {
        return false;
      }
      throw err;
    }
  }

  async unmark(input: { eventId: string; source: string }): Promise<void> {
    // deleteMany is idempotent: 0-row deletes do not throw. Matching by the
    // same (EventId, Source) pair the create used.
    await this.prisma.processedEvent.deleteMany({
      where: { EventId: input.eventId, Source: input.source },
    });
  }
}
