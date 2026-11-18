import { describe, expect, it } from 'vitest';
import { bindCustomerConsumers } from '../../src/application/event-handlers/customer-consumers.js';
import { INTENTIONALLY_IGNORED_EVENT_TYPES } from '../../src/infrastructure/rabbit/ignored-event-types.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { CustomerDirectoryRepository } from '../../src/application/ports/customer-directory-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../../src/application/ports/customer-preparer-assignment-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../../src/application/ports/customer-assignment-projection-repository.js';

/**
 * Notification (.NET) es el único dueño del aviso de import terminado o fallido: Communication no
 * registra handler para esos eventos y los descarta a propósito.
 */
function registeredEventTypes(): Set<string> {
  const handlers = new Set<string>();
  bindCustomerConsumers((eventType) => handlers.add(eventType), {
    emitter: {} as RealtimeEmitter,
    customerDirectory: {} as CustomerDirectoryRepository,
    customerPreparerAssignments: {} as CustomerPreparerAssignmentRepository,
    customerAssignments: {} as CustomerAssignmentProjectionRepository,
  } as Parameters<typeof bindCustomerConsumers>[1]);
  return handlers;
}

describe('aviso de import de clientes', () => {
  it.each(['customer.bulk_imported.v1', 'customer.bulk_import_failed.v1'])(
    'Communication no registra handler para %s',
    (eventType) => {
      expect(registeredEventTypes().has(eventType)).toBe(false);
    },
  );

  it.each([
    'BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomersBulkImportedIntegrationEvent',
    'BuildingBlocks.Messaging.CustomerIntegrationEvents.CustomerImportFailedIntegrationEvent',
  ])('%s se descarta a propósito', (clrType) => {
    expect(INTENTIONALLY_IGNORED_EVENT_TYPES.has(clrType)).toBe(true);
  });
});
