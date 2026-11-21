import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import {
  buildFileSourceDeclaredEvent,
  declareFileSource,
  relatedCustomerOf,
} from '../../src/application/services/file-source-declarer.js';
import { CloudStorageEventTypes } from '../../src/contracts/events/cloudstorage-events.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';

const u = () => randomUUID();

/**
 * La carga es la de `FileSourceDeclaredIntegrationEvent` (.NET, camelCase) con el sobre del outbox de Node. CloudStorage
 * lee `occurredOn` para la fecha de la declaración; el outbox, `occurredOnUtc`.
 */
describe('FileSourceDeclarer', () => {
  it('arma el evento con la identidad, las dos fechas iguales y los campos en camelCase', () => {
    const now = new Date('2026-11-20T15:04:05.000Z');
    const input = {
      tenantId: u(),
      correlationId: 'corr-1',
      fileId: u(),
      resourceType: 'conversation',
      resourceId: u(),
      role: 'attachment',
      relatedCustomerId: null,
    };

    const event = buildFileSourceDeclaredEvent(input, now);

    expect(event.eventType).toBe('cloudstorage.file_source_declared.v1');
    expect(CloudStorageEventTypes.FileSourceDeclared).toBe('cloudstorage.file_source_declared.v1');
    expect(event.occurredOnUtc).toBe('2026-11-20T15:04:05.000Z');
    expect(event.occurredOn).toBe(event.occurredOnUtc);
    expect(JSON.parse(JSON.stringify(event))).toEqual({
      eventId: event.eventId,
      eventType: 'cloudstorage.file_source_declared.v1',
      tenantId: input.tenantId,
      correlationId: 'corr-1',
      occurredOnUtc: '2026-11-20T15:04:05.000Z',
      occurredOn: '2026-11-20T15:04:05.000Z',
      fileId: input.fileId,
      sourceService: 'communication',
      sourceResourceType: 'conversation',
      sourceResourceId: input.resourceId,
      sourceRole: 'attachment',
      relatedCustomerId: null,
    });
  });

  it('lo encola en el publicador del outbox', async () => {
    const published: IntegrationEvent[] = [];
    const publisher = { enqueue: async (e: IntegrationEvent) => void published.push(e) };

    await declareFileSource(publisher, {
      tenantId: u(),
      correlationId: undefined,
      fileId: u(),
      resourceType: 'conversation',
      resourceId: u(),
      role: 'voice_note',
      relatedCustomerId: u(),
    });

    expect(published).toHaveLength(1);
    expect(published[0]).toMatchObject({
      eventType: 'cloudstorage.file_source_declared.v1',
      sourceRole: 'voice_note',
    });
  });

  it('el cliente relacionado es el único cliente vigente; con cero, varios o uno sin id, null', () => {
    const customer = u();
    const staff = { actorType: 'TenantEmployee', customerId: null, isRemoved: false };
    const client = (id: string | null, isRemoved = false) => ({
      actorType: 'CustomerPortal',
      customerId: id,
      isRemoved,
    });

    expect(relatedCustomerOf([staff, client(customer)])).toBe(customer);
    expect(relatedCustomerOf([staff, client(customer), client(customer)])).toBe(customer);
    expect(relatedCustomerOf([staff])).toBeNull();
    expect(relatedCustomerOf([staff, client(customer), client(u())])).toBeNull();
    expect(relatedCustomerOf([staff, client(customer), client(null)])).toBeNull();
    expect(relatedCustomerOf([staff, client(customer, true)])).toBeNull();
  });
});
