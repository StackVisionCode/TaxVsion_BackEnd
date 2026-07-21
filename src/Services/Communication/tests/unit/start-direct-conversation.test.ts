import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { startDirectConversation } from '../../src/application/use-cases/start-direct-conversation.js';
import { Conversation } from '../../src/domain/conversations/conversation.js';
import type { ConversationRepository } from '../../src/application/ports/conversation-repository.js';
import type { IdempotencyStore, IdempotencyReservation } from '../../src/application/ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../../src/application/ports/integration-event-publisher.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type {
  TenantSettingsProvider,
  TenantCommunicationSettingsSnapshot,
} from '../../src/application/ports/tenant-settings-provider.js';
import type {
  CustomerPortalAccountRepository,
  CustomerPortalAccountSnapshot,
} from '../../src/application/ports/customer-portal-account-repository.js';
import type {
  CustomerPreparerAssignmentRepository,
  CustomerPreparerAssignmentSnapshot,
} from '../../src/application/ports/customer-preparer-assignment-repository.js';

/**
 * Fase B6 (auditoria del plan de chat tipado) — el propio MD marca el test de
 * "gate en false -> comportamiento identico al actual sin importar si hay o
 * no asignacion" como *el test mas importante del track*, porque es la unica
 * forma de probar objetivamente que la Fase B5 no rompio ningun tenant
 * existente. Antes de esta fase, `start-direct-conversation.ts` (el archivo
 * mas modificado de todo el track — isPrimaryPreparer + el gate viven ahi)
 * no tenia NINGUN archivo de test propio.
 */

function u(): string {
  return randomUUID();
}

class FakeConversationRepository implements ConversationRepository {
  saved: Conversation[] = [];

  async save(conversation: Conversation): Promise<void> {
    this.saved.push(conversation);
  }
  async findById(): Promise<Conversation | null> {
    return null;
  }
  async findByUniquenessKey(): Promise<Conversation | null> {
    return null;
  }
  async listForUser() {
    return [];
  }
  async countForUser(): Promise<number> {
    return 0;
  }
  async listMessages() {
    return [];
  }
  async countUnreadForUser(): Promise<number> {
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

class FakeIntegrationEventPublisher implements IntegrationEventPublisher {
  readonly published: IntegrationEvent[] = [];
  async enqueue(event: IntegrationEvent): Promise<void> {
    this.published.push(event);
  }
}

function fakeSettings(overrides: Partial<TenantCommunicationSettingsSnapshot> = {}): TenantSettingsProvider {
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
        ...overrides,
      };
    },
  };
}

function fakePortalAccounts(byUserId: Record<string, CustomerPortalAccountSnapshot>): CustomerPortalAccountRepository {
  return {
    async upsert(): Promise<void> {},
    async markInactiveByUserId(): Promise<void> {},
    async findActiveByCustomerId(): Promise<CustomerPortalAccountSnapshot | null> {
      return null;
    },
    async findActiveByUserId(userId: string): Promise<CustomerPortalAccountSnapshot | null> {
      return byUserId[userId] ?? null;
    },
  };
}

function fakeAssignments(byCustomerId: Record<string, CustomerPreparerAssignmentSnapshot>): CustomerPreparerAssignmentRepository {
  return {
    async assign(): Promise<void> {},
    async unassign(): Promise<void> {},
    async findByCustomerId(_tenantId: string, customerId: string): Promise<CustomerPreparerAssignmentSnapshot | null> {
      return byCustomerId[customerId] ?? null;
    },
  };
}

function baseCommand(overrides: {
  tenantId: string;
  initiatorUserId: string;
  initiatorActorType: string;
  recipientUserId: string;
  recipientActorType: string;
}) {
  return {
    tenantId: overrides.tenantId,
    correlationId: u(),
    clientKey: u(),
    initiator: { userId: overrides.initiatorUserId, displayName: 'Initiator', actorType: overrides.initiatorActorType },
    recipient: { userId: overrides.recipientUserId, displayName: 'Recipient', actorType: overrides.recipientActorType },
  };
}

describe('startDirectConversation — regla dura B0/B5: gate en false no cambia el comportamiento actual', () => {
  it('permite un chat cliente-empleado aunque el empleado NO sea el preparador asignado, con el setting en su default (false)', async () => {
    const tenantId = u();
    const customerId = u();
    const customerUserId = u();
    const employeeUserId = u();
    const someOtherPreparerUserId = u();

    const conversations = new FakeConversationRepository();
    const deps = {
      conversations,
      idempotency: new FakeIdempotencyStore(),
      publisher: new FakeIntegrationEventPublisher(),
      settings: fakeSettings({ restrictCustomerChatToAssignedPreparer: false }),
      customerPortalAccounts: fakePortalAccounts({
        [customerUserId]: { customerId, tenantId, userId: customerUserId, isActive: true },
      }),
      // El cliente SI tiene un preparador asignado, pero a otro empleado — el
      // gate esta en false, asi que esto no debe importar en absoluto.
      customerPreparerAssignments: fakeAssignments({
        [customerId]: { customerId, tenantId, preparerUserId: someOtherPreparerUserId, assignedAtUtc: new Date() },
      }),
    };

    const result = await startDirectConversation(
      baseCommand({
        tenantId,
        initiatorUserId: customerUserId,
        initiatorActorType: 'CustomerPortal',
        recipientUserId: employeeUserId,
        recipientActorType: 'TenantEmployee',
      }),
      deps,
    );

    expect(result.isSuccess).toBe(true);
    expect(conversations.saved).toHaveLength(1);
  });

  it('permite un chat cliente-empleado cuando el cliente no tiene NINGUNA asignacion todavia, con el setting en false', async () => {
    const tenantId = u();
    const customerId = u();
    const customerUserId = u();
    const employeeUserId = u();

    const conversations = new FakeConversationRepository();
    const deps = {
      conversations,
      idempotency: new FakeIdempotencyStore(),
      publisher: new FakeIntegrationEventPublisher(),
      settings: fakeSettings({ restrictCustomerChatToAssignedPreparer: false }),
      customerPortalAccounts: fakePortalAccounts({
        [customerUserId]: { customerId, tenantId, userId: customerUserId, isActive: true },
      }),
      customerPreparerAssignments: fakeAssignments({}),
    };

    const result = await startDirectConversation(
      baseCommand({
        tenantId,
        initiatorUserId: customerUserId,
        initiatorActorType: 'CustomerPortal',
        recipientUserId: employeeUserId,
        recipientActorType: 'TenantEmployee',
      }),
      deps,
    );

    expect(result.isSuccess).toBe(true);
  });
});

describe('startDirectConversation — gate activo (restrictCustomerChatToAssignedPreparer=true)', () => {
  it('rechaza el chat si el destinatario NO es el preparador asignado', async () => {
    const tenantId = u();
    const customerId = u();
    const customerUserId = u();
    const wrongEmployeeUserId = u();
    const realPreparerUserId = u();

    const deps = {
      conversations: new FakeConversationRepository(),
      idempotency: new FakeIdempotencyStore(),
      publisher: new FakeIntegrationEventPublisher(),
      settings: fakeSettings({ restrictCustomerChatToAssignedPreparer: true }),
      customerPortalAccounts: fakePortalAccounts({
        [customerUserId]: { customerId, tenantId, userId: customerUserId, isActive: true },
      }),
      customerPreparerAssignments: fakeAssignments({
        [customerId]: { customerId, tenantId, preparerUserId: realPreparerUserId, assignedAtUtc: new Date() },
      }),
    };

    const result = await startDirectConversation(
      baseCommand({
        tenantId,
        initiatorUserId: customerUserId,
        initiatorActorType: 'CustomerPortal',
        recipientUserId: wrongEmployeeUserId,
        recipientActorType: 'TenantEmployee',
      }),
      deps,
    );

    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) {
      expect(result.error.code).toBe('Chat.NotAssignedPreparer');
    }
  });

  it('rechaza el chat si el cliente no tiene ninguna asignacion (isPrimaryPreparer nunca puede ser true)', async () => {
    const tenantId = u();
    const customerId = u();
    const customerUserId = u();
    const employeeUserId = u();

    const deps = {
      conversations: new FakeConversationRepository(),
      idempotency: new FakeIdempotencyStore(),
      publisher: new FakeIntegrationEventPublisher(),
      settings: fakeSettings({ restrictCustomerChatToAssignedPreparer: true }),
      customerPortalAccounts: fakePortalAccounts({
        [customerUserId]: { customerId, tenantId, userId: customerUserId, isActive: true },
      }),
      customerPreparerAssignments: fakeAssignments({}),
    };

    const result = await startDirectConversation(
      baseCommand({
        tenantId,
        initiatorUserId: customerUserId,
        initiatorActorType: 'CustomerPortal',
        recipientUserId: employeeUserId,
        recipientActorType: 'TenantEmployee',
      }),
      deps,
    );

    expect(result.isSuccess).toBe(false);
    if (!result.isSuccess) {
      expect(result.error.code).toBe('Chat.NotAssignedPreparer');
    }
  });

  it('permite el chat y marca isPrimaryPreparer=true cuando el destinatario SI es el preparador asignado', async () => {
    const tenantId = u();
    const customerId = u();
    const customerUserId = u();
    const preparerUserId = u();

    const conversations = new FakeConversationRepository();
    const deps = {
      conversations,
      idempotency: new FakeIdempotencyStore(),
      publisher: new FakeIntegrationEventPublisher(),
      settings: fakeSettings({ restrictCustomerChatToAssignedPreparer: true }),
      customerPortalAccounts: fakePortalAccounts({
        [customerUserId]: { customerId, tenantId, userId: customerUserId, isActive: true },
      }),
      customerPreparerAssignments: fakeAssignments({
        [customerId]: { customerId, tenantId, preparerUserId, assignedAtUtc: new Date() },
      }),
    };

    const result = await startDirectConversation(
      baseCommand({
        tenantId,
        initiatorUserId: customerUserId,
        initiatorActorType: 'CustomerPortal',
        recipientUserId: preparerUserId,
        recipientActorType: 'TenantEmployee',
      }),
      deps,
    );

    expect(result.isSuccess).toBe(true);
    expect(conversations.saved).toHaveLength(1);
    const snapshot = conversations.saved[0]!.toSnapshot();
    const recipientParticipant = snapshot.participants.find((p) => p.userId === preparerUserId);
    expect(recipientParticipant?.isPrimaryPreparer).toBe(true);
  });
});

describe('startDirectConversation — isPrimaryPreparer end-to-end (independiente del gate)', () => {
  it('resuelve isPrimaryPreparer=true a partir de la asignacion real, con el gate en false', async () => {
    const tenantId = u();
    const customerId = u();
    const customerUserId = u();
    const preparerUserId = u();

    const conversations = new FakeConversationRepository();
    const deps = {
      conversations,
      idempotency: new FakeIdempotencyStore(),
      publisher: new FakeIntegrationEventPublisher(),
      settings: fakeSettings({ restrictCustomerChatToAssignedPreparer: false }),
      customerPortalAccounts: fakePortalAccounts({
        [customerUserId]: { customerId, tenantId, userId: customerUserId, isActive: true },
      }),
      customerPreparerAssignments: fakeAssignments({
        [customerId]: { customerId, tenantId, preparerUserId, assignedAtUtc: new Date() },
      }),
    };

    const result = await startDirectConversation(
      baseCommand({
        tenantId,
        initiatorUserId: customerUserId,
        initiatorActorType: 'CustomerPortal',
        recipientUserId: preparerUserId,
        recipientActorType: 'TenantEmployee',
      }),
      deps,
    );

    expect(result.isSuccess).toBe(true);
    const snapshot = conversations.saved[0]!.toSnapshot();
    const recipientParticipant = snapshot.participants.find((p) => p.userId === preparerUserId);
    expect(recipientParticipant?.isPrimaryPreparer).toBe(true);
  });

  it('resuelve isPrimaryPreparer=false cuando ninguno de los dos lados es CustomerPortal', async () => {
    const tenantId = u();
    const employeeAUserId = u();
    const employeeBUserId = u();

    const conversations = new FakeConversationRepository();
    const deps = {
      conversations,
      idempotency: new FakeIdempotencyStore(),
      publisher: new FakeIntegrationEventPublisher(),
      settings: fakeSettings({ restrictCustomerChatToAssignedPreparer: false, employeeToEmployeeChatEnabled: true }),
      customerPortalAccounts: fakePortalAccounts({}),
      customerPreparerAssignments: fakeAssignments({}),
    };

    const result = await startDirectConversation(
      baseCommand({
        tenantId,
        initiatorUserId: employeeAUserId,
        initiatorActorType: 'TenantEmployee',
        recipientUserId: employeeBUserId,
        recipientActorType: 'TenantEmployee',
      }),
      deps,
    );

    expect(result.isSuccess).toBe(true);
    const snapshot = conversations.saved[0]!.toSnapshot();
    const recipientParticipant = snapshot.participants.find((p) => p.userId === employeeBUserId);
    expect(recipientParticipant?.isPrimaryPreparer).toBe(false);
  });
});
