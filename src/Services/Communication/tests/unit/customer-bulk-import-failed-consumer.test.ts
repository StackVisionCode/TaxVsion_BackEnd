import { describe, expect, it, vi } from 'vitest';
import { bindCustomerConsumers } from '../../src/application/event-handlers/customer-consumers.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { CustomerDirectoryRepository } from '../../src/application/ports/customer-directory-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../../src/application/ports/customer-preparer-assignment-repository.js';

/**
 * Test de contrato (regla de la Fase 0 del plan de notificaciones): los payloads de
 * abajo usan los nombres de campo EXACTOS que .NET serializa hoy (PascalCase, copiados
 * literalmente de CustomerImportFailedIntegrationEvent en
 * BuildingBlocks/Messaging/CustomerIntegrationEvents/ — Fase 1B: antes de esta fase el
 * camino de falla del bulk-import no publicaba ningun evento en absoluto).
 */
function setup() {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(eventType, handler);
  };
  const notifications: NotificationRepository = {
    createIfMissing: vi.fn().mockResolvedValue(true),
    findById: vi.fn(),
    update: vi.fn(),
    listForUser: vi.fn(),
    countUnread: vi.fn().mockResolvedValue(0),
  };
  const emitter: RealtimeEmitter = {
    emitToUser: vi.fn(),
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToTenant: vi.fn(),
  } as unknown as RealtimeEmitter;
  const customerDirectory: CustomerDirectoryRepository = {
    upsert: vi.fn(),
    findByCustomerId: vi.fn(),
    markInactive: vi.fn(),
  } as unknown as CustomerDirectoryRepository;
  const customerPreparerAssignments: CustomerPreparerAssignmentRepository = {
    assign: vi.fn(),
    unassign: vi.fn(),
    findByCustomerId: vi.fn(),
  };

  bindCustomerConsumers(register, { notifications, emitter, customerDirectory, customerPreparerAssignments });
  return { handlers, notifications, emitter };
}

function envelope(eventType: string, payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType,
    tenantId: 'tenant-1',
    correlationId: 'corr-1',
    occurredOnUtc: new Date().toISOString(),
    payload,
  };
}

describe('bindCustomerConsumers — customer.bulk_import_failed.v1 (Fase 1B)', () => {
  it('crea la notificacion para CreatedByUserId con el Reason en el body', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('customer.bulk_import_failed.v1')!(
      envelope('customer.bulk_import_failed.v1', {
        ImportJobId: 'job-1',
        CreatedByUserId: 'employee-1',
        Reason: 'File is empty.',
        FailedAtUtc: new Date().toISOString(),
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    const call = vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!;
    expect(call.userId).toBe('employee-1');
    expect(call.toSnapshot().body).toContain('File is empty.');
  });

  it('no crea ninguna notificacion si CreatedByUserId falta', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('customer.bulk_import_failed.v1')!(
      envelope('customer.bulk_import_failed.v1', {
        ImportJobId: 'job-2',
        Reason: 'Uploaded file failed the security scan.',
        FailedAtUtc: new Date().toISOString(),
      }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });
});
