import { describe, expect, it, vi } from 'vitest';
import type { PrismaClient } from '@prisma/client';
import { PrismaOutboxPublisher } from '../../src/infrastructure/persistence/prisma-outbox-publisher.js';

/** .NET acepta X-Correlation-Id de hasta 128; la columna del outbox es NVarChar(64) y el INSERT fallaba. */
describe('PrismaOutboxPublisher', () => {
  function publisher() {
    const create = vi.fn().mockResolvedValue(undefined);
    const prisma = { outboxMessage: { create } } as unknown as PrismaClient;
    return { outbox: new PrismaOutboxPublisher(prisma), create };
  }

  const event = (correlationId: string) => ({
    eventId: '00000000-0000-0000-0000-000000000001',
    eventType: 'communication.meeting.cancelled.v1',
    tenantId: '00000000-0000-0000-0000-000000000002',
    occurredOnUtc: '2026-11-07T10:00:00.000Z',
    correlationId,
  });

  it('recorta a 64 un CorrelationId largo', async () => {
    const { outbox, create } = publisher();

    await outbox.enqueue(event('x'.repeat(128)));

    expect(create).toHaveBeenCalledWith({ data: expect.objectContaining({ CorrelationId: 'x'.repeat(64) }) });
  });

  it('deja intacto uno corto', async () => {
    const { outbox, create } = publisher();

    await outbox.enqueue(event('corr-1'));

    expect(create).toHaveBeenCalledWith({ data: expect.objectContaining({ CorrelationId: 'corr-1' }) });
  });
});
