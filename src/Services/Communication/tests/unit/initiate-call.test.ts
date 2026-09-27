import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call, type CallSnapshot } from '../../src/domain/calls/call.js';
import type { CallRepository } from '../../src/application/ports/call-repository.js';
import type { IdempotencyReservation, IdempotencyStore } from '../../src/application/ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type {
  TenantCommunicationSettingsSnapshot,
  TenantSettingsProvider,
} from '../../src/application/ports/tenant-settings-provider.js';
import { initiateCall } from '../../src/application/use-cases/initiate-call.js';

function u(): string {
  return randomUUID();
}

class FakeCallRepository implements CallRepository {
  private readonly store = new Map<string, Call>();
  async save(call: Call): Promise<void> {
    this.store.set(call.id, call);
  }
  async findById(tenantId: string, callId: string): Promise<Call | null> {
    const call = this.store.get(callId);
    return call && call.tenantId === tenantId ? call : null;
  }
  async findRingingOlderThan(): Promise<CallSnapshot[]> {
    return [];
  }
  async listRecentForUser(): Promise<CallSnapshot[]> {
    return [];
  }
  async countRecentForUser(): Promise<number> {
    return 0;
  }
}

class FakeIdempotencyStore implements IdempotencyStore {
  async tryReserve<T>(): Promise<IdempotencyReservation<T>> {
    return { status: 'fresh', token: u() };
  }
  async commit(): Promise<void> {}
  async release(): Promise<void> {}
}

class FakePublisher implements IntegrationEventPublisher {
  readonly published: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.published.push(event);
  }
}

function fakeSettings(): TenantSettingsProvider {
  return {
    async get(tenantId: string): Promise<TenantCommunicationSettingsSnapshot> {
      return {
        tenantId,
        chatEnabled: true,
        employeeToEmployeeChatEnabled: true,
        restrictCustomerChatToAssignedPreparer: false,
        screenshotsEnabled: true,
        internalGroupsEnabled: true,
        messageRetentionDays: 365,
      };
    },
  };
}

function deps() {
  return {
    calls: new FakeCallRepository(),
    idempotency: new FakeIdempotencyStore(),
    publisher: new FakePublisher(),
    settings: fakeSettings(),
  };
}

function command(caller: { actorType: string }, callee: { actorType: string | null }) {
  return {
    tenantId: u(),
    correlationId: u(),
    clientKey: u(),
    kind: 'Audio' as const,
    caller: { userId: u(), displayName: 'Caller', actorType: caller.actorType },
    callee: { userId: u(), displayName: 'Callee', actorType: callee.actorType },
  };
}

describe('initiateCall — un cliente del portal solo llama a la oficina', () => {
  it('rechaza la llamada de un cliente a otro cliente', async () => {
    const result = await initiateCall(command({ actorType: 'CustomerPortal' }, { actorType: 'CustomerPortal' }), deps());

    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) {
      expect(result.error.code).toBe('Call.CustomerToCustomerNotAllowed');
    }
  });

  it('rechaza la llamada de un cliente a un invitado de meeting', async () => {
    const result = await initiateCall(command({ actorType: 'CustomerPortal' }, { actorType: 'Guest' }), deps());

    expect(result.isSuccess).toBe(false);
  });

  it('rechaza la llamada de un cliente cuando el destinatario no esta en el directorio local', async () => {
    // Fail-closed: sin actor type resuelto no se puede demostrar que el destinatario es la oficina.
    const result = await initiateCall(command({ actorType: 'CustomerPortal' }, { actorType: null }), deps());

    expect(result.isSuccess).toBe(false);
  });

  it('permite la llamada de un cliente a un empleado', async () => {
    const result = await initiateCall(command({ actorType: 'CustomerPortal' }, { actorType: 'TenantEmployee' }), deps());

    expect(result.isSuccess).toBe(true);
  });

  it('permite la llamada de un empleado a un cliente', async () => {
    const result = await initiateCall(command({ actorType: 'TenantEmployee' }, { actorType: 'CustomerPortal' }), deps());

    expect(result.isSuccess).toBe(true);
  });

  it('permite la llamada entre dos empleados', async () => {
    const result = await initiateCall(command({ actorType: 'TenantEmployee' }, { actorType: 'TenantAdmin' }), deps());

    expect(result.isSuccess).toBe(true);
  });
});
