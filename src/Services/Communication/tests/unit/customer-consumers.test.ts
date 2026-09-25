import { describe, expect, it, vi } from 'vitest';
import { bindCustomerConsumers } from '../../src/application/event-handlers/customer-consumers.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { CustomerDirectoryRepository } from '../../src/application/ports/customer-directory-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../../src/application/ports/customer-preparer-assignment-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../../src/application/ports/customer-assignment-projection-repository.js';

/**
 * F4 (backlog 5.1) — al aplicar cada customer.*.v1 el consumer emite `customer.changed` a todo el
 * tenant (`t:{tenantId}`) para que el cache de clientes del front invalide ese cliente.
 */
function setup() {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(eventType, handler);
  };
  const notifications = {
    createIfMissing: vi.fn().mockResolvedValue(true),
    findById: vi.fn(),
    update: vi.fn(),
    listForUser: vi.fn(),
    countUnread: vi.fn().mockResolvedValue(0),
  } as unknown as NotificationRepository;
  const emitter = {
    emitToUser: vi.fn(),
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToTenant: vi.fn(),
  } as unknown as RealtimeEmitter;
  const customerDirectory = {
    upsert: vi.fn(),
    findByCustomerId: vi.fn(),
    markInactive: vi.fn(),
  } as unknown as CustomerDirectoryRepository;
  const customerPreparerAssignments = {
    assign: vi.fn(),
    unassign: vi.fn(),
    findByCustomerId: vi.fn(),
  } as unknown as CustomerPreparerAssignmentRepository;
  const customerAssignments = {
    getVersion: vi.fn().mockResolvedValue(null),
    replace: vi.fn(),
    isAssigned: vi.fn(),
  } as unknown as CustomerAssignmentProjectionRepository;

  bindCustomerConsumers(register, {
    notifications,
    emitter,
    customerDirectory,
    customerPreparerAssignments,
    customerAssignments,
  });
  return { handlers, emitter, customerAssignments };
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

describe('bindCustomerConsumers — customer.changed realtime (F4)', () => {
  const cases: { event: string; payload: Record<string, unknown>; changeType: string }[] = [
    {
      event: 'customer.created.v1',
      payload: { customerId: 'c-1', displayName: 'Ada', primaryEmail: 'ada@example.com' },
      changeType: 'created',
    },
    {
      event: 'customer.updated.v1',
      payload: { customerId: 'c-1', displayName: 'Ada L', primaryEmail: 'ada@example.com' },
      changeType: 'updated',
    },
    { event: 'customer.deactivated.v1', payload: { customerId: 'c-1' }, changeType: 'deactivated' },
    { event: 'customer.archived.v1', payload: { customerId: 'c-1' }, changeType: 'archived' },
  ];

  for (const { event, payload, changeType } of cases) {
    it(`${event} emite customer.changed al tenant con changeType=${changeType}`, async () => {
      const { handlers, emitter } = setup();

      await handlers.get(event)!(envelope(event, payload));

      expect(emitter.emitToTenant).toHaveBeenCalledTimes(1);
      const call = vi.mocked(emitter.emitToTenant).mock.calls[0]![0] as {
        tenantId: string;
        event: string;
        envelope: { payload: { customerId: string; changeType: string } };
      };
      expect(call.tenantId).toBe('tenant-1');
      expect(call.event).toBe('customer.changed');
      expect(call.envelope.payload).toEqual({ customerId: 'c-1', changeType });
    });
  }

  it('no emite si falta customerId', async () => {
    const { handlers, emitter } = setup();

    await handlers.get('customer.created.v1')!(
      envelope('customer.created.v1', { displayName: 'Ada', primaryEmail: 'ada@example.com' }),
    );

    expect(emitter.emitToTenant).not.toHaveBeenCalled();
  });
});

describe('bindCustomerConsumers — customer.assignments_changed.v1 (P2.5, proyeccion M:N)', () => {
  it('reemplaza el set de asignados cuando el snapshot es nuevo', async () => {
    const { handlers, customerAssignments } = setup();
    vi.mocked(customerAssignments.getVersion).mockResolvedValue(null);

    await handlers.get('customer.assignments_changed.v1')!(
      envelope('customer.assignments_changed.v1', {
        customerId: 'c-1',
        assigneeUserIds: ['u-1', 'u-2'],
        version: '2026-09-24T10:00:00.000Z',
      }),
    );

    expect(customerAssignments.replace).toHaveBeenCalledTimes(1);
    const call = vi.mocked(customerAssignments.replace).mock.calls[0]!;
    expect(call[0]).toBe('tenant-1');
    expect(call[1]).toBe('c-1');
    expect(call[2]).toEqual(['u-1', 'u-2']);
    expect(call[3]).toEqual(new Date('2026-09-24T10:00:00.000Z'));
  });

  it('ignora un snapshot igual o mas viejo que el ya aplicado (version guard)', async () => {
    const { handlers, customerAssignments } = setup();
    vi.mocked(customerAssignments.getVersion).mockResolvedValue(new Date('2026-09-24T12:00:00.000Z'));

    await handlers.get('customer.assignments_changed.v1')!(
      envelope('customer.assignments_changed.v1', {
        customerId: 'c-1',
        assigneeUserIds: ['u-1'],
        version: '2026-09-24T10:00:00.000Z',
      }),
    );

    expect(customerAssignments.replace).not.toHaveBeenCalled();
  });

  it('no hace nada si falta customerId o version', async () => {
    const { handlers, customerAssignments } = setup();

    await handlers.get('customer.assignments_changed.v1')!(
      envelope('customer.assignments_changed.v1', { assigneeUserIds: ['u-1'] }),
    );

    expect(customerAssignments.replace).not.toHaveBeenCalled();
  });
});
