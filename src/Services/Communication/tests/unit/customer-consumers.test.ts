import { describe, expect, it, vi } from 'vitest';
import { bindCustomerConsumers } from '../../src/application/event-handlers/customer-consumers.js';
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
  const emitter = {
    emitToUser: vi.fn(),
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToTenant: vi.fn(),
    emitToTenantStaff: vi.fn(),
    emitToTenantMembers: vi.fn(),
    emitBroadcast: vi.fn(),
  } as unknown as RealtimeEmitter;
  const customerDirectory = {
    upsert: vi.fn(),
    findByCustomerId: vi.fn(),
    markInactive: vi.fn(),
    markActive: vi.fn(),
    clearAvatar: vi.fn(),
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
    emitter,
    customerDirectory,
    customerPreparerAssignments,
    customerAssignments,
  });
  return { handlers, emitter, customerAssignments, customerDirectory };
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
    { event: 'customer.reactivated.v1', payload: { customerId: 'c-1' }, changeType: 'reactivated' },
    { event: 'customer.activated.v1', payload: { customerId: 'c-1' }, changeType: 'activated' },
  ];

  for (const { event, payload, changeType } of cases) {
    it(`${event} emite customer.changed solo al personal con changeType=${changeType}`, async () => {
      const { handlers, emitter } = setup();

      await handlers.get(event)!(envelope(event, payload));

      expect(emitter.emitToTenantStaff).toHaveBeenCalledTimes(1);
      // Nunca al room del tenant entero: los clientes del portal no ven la cartera de la oficina.
      expect(emitter.emitToTenant).not.toHaveBeenCalled();
      const call = vi.mocked(emitter.emitToTenantStaff).mock.calls[0]![0] as {
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

    expect(emitter.emitToTenantStaff).not.toHaveBeenCalled();
  });
});

// Cliente restaurado (Archived→Active) o activado (Inactive→Active): sin handler seguía inactivo en el
// directorio hasta la reconciliación. Mismo control de orden que los avisos de Auth: fecha del sobre.
describe('bindCustomerConsumers — estado del cliente en el directorio', () => {
  const at = '2026-11-01T10:00:00.000Z';
  const cases: Array<[string, 'markActive' | 'markInactive']> = [
    ['customer.reactivated.v1', 'markActive'],
    ['customer.activated.v1', 'markActive'],
    ['customer.deactivated.v1', 'markInactive'],
    ['customer.archived.v1', 'markInactive'],
  ];
  for (const [event, method] of cases) {
    it(`${event} llama ${method} con la fecha del aviso`, async () => {
      const { handlers, customerDirectory } = setup();

      await handlers.get(event)!({ ...envelope(event, { customerId: 'c-1' }), occurredOnUtc: at });

      expect(customerDirectory[method]).toHaveBeenCalledWith('c-1', new Date(at));
    });
  }
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

// F-E (email opcional): el cliente sin email entra al directorio; el email llega después en customer.updated.v1,
// que trae el estado para no dejar activa una fila de un cliente que ya se desactivó (E-3).
describe('bindCustomerConsumers — cliente sin email', () => {
  const at = '2026-11-18T10:00:00.000Z';

  it('customer.created.v1 sin email entra al directorio con email null y la fecha del aviso', async () => {
    const { handlers, customerDirectory } = setup();

    await handlers.get('customer.created.v1')!({
      ...envelope('customer.created.v1', { customerId: 'c-1', displayName: 'Ada', primaryEmail: null }),
      occurredOnUtc: at,
    });

    expect(customerDirectory.upsert).toHaveBeenCalledWith({
      customerId: 'c-1',
      tenantId: 'tenant-1',
      displayName: 'Ada',
      email: null,
      isActive: true,
      occurredAtUtc: new Date(at),
    });
  });

  it('customer.updated.v1 con el email nuevo y estado Inactive deja la fila inactiva', async () => {
    const { handlers, customerDirectory } = setup();
    vi.mocked(customerDirectory.findByCustomerId).mockResolvedValue(null);

    await handlers.get('customer.updated.v1')!({
      ...envelope('customer.updated.v1', {
        customerId: 'c-1',
        displayName: 'Ada',
        primaryEmail: 'late@example.com',
        status: 'Inactive',
      }),
      occurredOnUtc: at,
    });

    expect(customerDirectory.upsert).toHaveBeenCalledWith({
      customerId: 'c-1',
      tenantId: 'tenant-1',
      displayName: 'Ada',
      email: 'late@example.com',
      isActive: false,
      occurredAtUtc: new Date(at),
    });
  });

  it('customer.updated.v1 sin estado (mensaje viejo) no toca el estado guardado', async () => {
    const { handlers, customerDirectory } = setup();
    vi.mocked(customerDirectory.findByCustomerId).mockResolvedValue(null);

    await handlers.get('customer.updated.v1')!({
      ...envelope('customer.updated.v1', { customerId: 'c-1', displayName: 'Ada', primaryEmail: null }),
      occurredOnUtc: at,
    });

    const call = vi.mocked(customerDirectory.upsert).mock.calls[0]![0];
    expect(call.email).toBeNull();
    expect(call.isActive).toBeUndefined();
  });

  it('sin nombre no se proyecta', async () => {
    const { handlers, customerDirectory } = setup();

    await handlers.get('customer.created.v1')!(
      envelope('customer.created.v1', { customerId: 'c-1', primaryEmail: 'a@example.com' }),
    );

    expect(customerDirectory.upsert).not.toHaveBeenCalled();
  });
});

// Cada bloque guardado del import trae la ficha de sus clientes: el directorio se llena sin esperar la reconciliación.
describe('bindCustomerConsumers — customer.import_chunk_committed.v1', () => {
  const event = 'customer.import_chunk_committed.v1';
  function handlerOf(handlers: Map<string, (env: IncomingEnvelope) => Promise<void>>) {
    const handler = handlers.get(event);
    if (!handler) throw new Error(`${event} sin handler`);
    return handler;
  }

  it('proyecta cada cliente del bloque con su estado y su foto', async () => {
    const { handlers, customerDirectory } = setup();

    await handlerOf(handlers)(
      envelope(event, {
        importJobId: 'job-1',
        chunkNumber: 1,
        customers: [
          {
            customerId: 'c-1',
            isNew: true,
            displayName: 'Ada',
            primaryEmail: 'ada@example.com',
            status: 'Active',
          },
          {
            CustomerId: 'c-2',
            IsNew: false,
            DisplayName: 'Grace',
            PrimaryEmail: 'grace@example.com',
            Status: 'Inactive',
            AvatarFileId: 'AAAAAAAA-0000-0000-0000-000000000001',
          },
        ],
      }),
    );

    expect(customerDirectory.upsert).toHaveBeenCalledTimes(2);
    expect(customerDirectory.upsert).toHaveBeenNthCalledWith(1, {
      customerId: 'c-1',
      tenantId: 'tenant-1',
      displayName: 'Ada',
      email: 'ada@example.com',
      isActive: true,
      occurredAtUtc: expect.any(Date),
    });
    expect(customerDirectory.upsert).toHaveBeenNthCalledWith(2, {
      customerId: 'c-2',
      tenantId: 'tenant-1',
      displayName: 'Grace',
      email: 'grace@example.com',
      isActive: false,
      avatarFileId: 'aaaaaaaa-0000-0000-0000-000000000001',
      occurredAtUtc: expect.any(Date),
    });
  });

  // F-E: sin email el cliente igual entra al directorio (sale en el rail y en el chat por su nombre).
  it('proyecta la ficha sin email y salta solo la que no tiene nombre o id', async () => {
    const { handlers, customerDirectory } = setup();

    await handlerOf(handlers)(
      envelope(event, {
        customers: [
          { customerId: 'c-1', isNew: true, displayName: 'Ada', primaryEmail: null, status: 'Active' },
          { customerId: 'c-2', isNew: true, primaryEmail: 'x@example.com', status: 'Active' },
          { isNew: true, displayName: 'Sin id', primaryEmail: 'y@example.com', status: 'Active' },
        ],
      }),
    );

    expect(customerDirectory.upsert).toHaveBeenCalledTimes(1);
    expect(customerDirectory.upsert).toHaveBeenCalledWith(
      expect.objectContaining({ customerId: 'c-1', displayName: 'Ada', email: null, isActive: true }),
    );
  });

  // Los nuevos no están en el cache del CRM; los actualizados sí y se invalidan. Sin ráfaga de 500 avisos.
  it('avisa al CRM solo por los clientes existentes que cambiaron', async () => {
    const { handlers, emitter } = setup();

    await handlerOf(handlers)(
      envelope(event, {
        customers: [
          {
            customerId: 'c-1',
            isNew: true,
            displayName: 'Ada',
            primaryEmail: 'ada@example.com',
            status: 'Active',
          },
          {
            customerId: 'c-2',
            isNew: false,
            displayName: 'Grace',
            primaryEmail: 'g@example.com',
            status: 'Active',
          },
        ],
      }),
    );

    expect(emitter.emitToTenantStaff).toHaveBeenCalledTimes(1);
    const [call] = vi.mocked(emitter.emitToTenantStaff).mock.calls[0] ?? [];
    expect((call as { envelope: { payload: unknown } } | undefined)?.envelope.payload).toEqual({
      customerId: 'c-2',
      changeType: 'updated',
    });
  });

  it('no hace nada sin oficina', async () => {
    const { handlers, customerDirectory } = setup();

    await handlerOf(handlers)({
      ...envelope(event, {
        customers: [
          { customerId: 'c-1', isNew: true, displayName: 'Ada', primaryEmail: 'a@e.com', status: 'Active' },
        ],
      }),
      tenantId: '',
    });

    expect(customerDirectory.upsert).not.toHaveBeenCalled();
  });
});
