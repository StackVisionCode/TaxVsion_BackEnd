import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { describe, expect, it, vi } from 'vitest';
import {
  bindCorrespondenceConsumers,
  parseCorrespondenceCustomerEmailReceived,
  parseCorrespondenceThreadStateChanged,
} from '../../src/application/event-handlers/correspondence-consumers.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type {
  UserPermissionsProjectionRepository,
  UserPermissionsProjectionSnapshot,
} from '../../src/application/ports/user-permissions-projection-repository.js';
import { MailSocketEvents } from '../../src/contracts/socket/mail-socket-events.js';

const customerId = '1a2b3c4d-3333-4c4d-9e5f-000000000003';
const emailId = '7e8f9a0b-4444-4d5e-8f60-000000000004';
const threadId = '9b0d2c4e-2222-4b3c-8d4e-000000000002';
const accountId = '55bc55cd-5555-4555-8555-000000000005';
const ownerId = 'a77d6953-f806-4518-96b7-35734f515e3f';

function snapshot(userId: string, permissions: readonly string[]): UserPermissionsProjectionSnapshot {
  return {
    userId,
    tenantId: 'tenant-1',
    permissions,
    permissionVersion: 1,
    roleIds: [],
    actorType: 'TenantEmployee',
    isActive: true,
    updatedAtUtc: new Date(),
  };
}

function setup(permissionRows: Record<string, readonly UserPermissionsProjectionSnapshot[]> = {}) {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(eventType, handler);
  };
  const emitter = {
    emitToUser: vi.fn(),
    emitToTenant: vi.fn(),
    emitToTenantStaff: vi.fn(),
  } as unknown as RealtimeEmitter;
  const userPermissions = {
    findActiveByTenantAndPermission: vi.fn(
      async (_tenantId: string, permission: string) => permissionRows[permission] ?? [],
    ),
  } as unknown as UserPermissionsProjectionRepository;

  bindCorrespondenceConsumers(register, { emitter, userPermissions });
  return { handlers, emitter, userPermissions };
}

function envelope(payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType: 'correspondence.customer_email_received.v1',
    tenantId: 'tenant-1',
    correlationId: 'corr-1',
    occurredOnUtc: new Date().toISOString(),
    payload,
  };
}

function correspondenceHandler(
  handlers: Map<string, (env: IncomingEnvelope) => Promise<void>>,
): (env: IncomingEnvelope) => Promise<void> {
  const handler = handlers.get('correspondence.customer_email_received.v1');
  expect(handler).toBeDefined();
  return handler as (env: IncomingEnvelope) => Promise<void>;
}

describe('correspondence.customer_email_received.v1', () => {
  it('emits a personal mailbox update only to its owner', async () => {
    const { handlers, emitter, userPermissions } = setup();

    await correspondenceHandler(handlers)(
      envelope({
        CustomerId: customerId,
        IncomingEmailId: emailId,
        EmailThreadId: threadId,
        AccountId: accountId,
        MailboxOwnerUserId: ownerId,
      }),
    );

    expect(userPermissions.findActiveByTenantAndPermission).not.toHaveBeenCalled();
    expect(emitter.emitToUser).toHaveBeenCalledTimes(1);
    expect(emitter.emitToUser).toHaveBeenCalledWith(
      expect.objectContaining({
        tenantId: 'tenant-1',
        userId: ownerId,
        event: MailSocketEvents.IncomingEmail,
      }),
    );
    expect(emitter.emitToTenant).not.toHaveBeenCalled();
    expect(emitter.emitToTenantStaff).not.toHaveBeenCalled();
  });

  it('emits an office mailbox update only to active readers and writers', async () => {
    const readerId = '11111111-1111-4111-8111-111111111111';
    const writerId = '22222222-2222-4222-8222-222222222222';
    const duplicate = snapshot(readerId, ['connectors.accounts.write']);
    const { handlers, emitter } = setup({
      'connectors.accounts.office.read': [snapshot(readerId, ['connectors.accounts.office.read'])],
      'connectors.accounts.write': [duplicate, snapshot(writerId, ['connectors.accounts.write'])],
    });

    await correspondenceHandler(handlers)(
      envelope({
        customerId,
        incomingEmailId: emailId,
        emailThreadId: threadId,
        accountId,
        mailboxOwnerUserId: null,
      }),
    );

    expect(emitter.emitToUser).toHaveBeenCalledTimes(2);
    expect(
      vi
        .mocked(emitter.emitToUser)
        .mock.calls.map(([call]) => call.userId)
        .sort(),
    ).toEqual([readerId, writerId].sort());
    expect(emitter.emitToTenantStaff).not.toHaveBeenCalled();
  });

  it('fails closed for an old event without mailbox fields', async () => {
    const { handlers, emitter } = setup();

    await correspondenceHandler(handlers)(
      envelope({ CustomerId: customerId, IncomingEmailId: emailId, EmailThreadId: threadId }),
    );

    expect(emitter.emitToUser).not.toHaveBeenCalled();
    expect(emitter.emitToTenantStaff).not.toHaveBeenCalled();
  });

  it('parses the shared golden fixture', () => {
    const fixturePath = fileURLToPath(
      new URL(
        '../../../../BuildingBlocks/Messaging/Contracts/Fixtures/correspondence.customer_email_received.v1.json',
        import.meta.url,
      ),
    );
    const payload = JSON.parse(readFileSync(fixturePath, 'utf8')) as Record<string, unknown>;

    const parsed = parseCorrespondenceCustomerEmailReceived(payload);

    expect(parsed.success).toBe(true);
    if (parsed.success) {
      expect(parsed.data.accountId).toBe(accountId);
      expect(parsed.data.mailboxOwnerUserId).toBe(ownerId);
    }
  });
});

describe('correspondence.thread_state_changed.v1', () => {
  const actorId = 'c3c3c3c3-6666-4666-8666-000000000006';
  const officeReaderId = 'd4d4d4d4-7777-4777-8777-000000000007';
  const stateEnvelope = (payload: Record<string, unknown>): IncomingEnvelope => ({
    ...envelope(payload),
    eventType: 'correspondence.thread_state_changed.v1',
  });

  it('emits the thread state to office mail readers and to the actor, once each', async () => {
    const { handlers, emitter } = setup({
      'connectors.accounts.office.read': [snapshot(officeReaderId, []), snapshot(actorId, [])],
    });
    const handler = handlers.get('correspondence.thread_state_changed.v1');
    expect(handler).toBeDefined();

    await handler!(
      stateEnvelope({
        CustomerId: customerId,
        EmailThreadId: threadId,
        Status: 'Archived',
        UnreadCount: 0,
        MessageCount: 3,
        ActorUserId: actorId,
      }),
    );

    const calls = vi.mocked(emitter.emitToUser).mock.calls.map(([input]) => input);
    expect(calls.map((input) => input.userId).sort()).toEqual([actorId, officeReaderId].sort());
    expect(calls[0]?.event).toBe(MailSocketEvents.ThreadState);
    expect(calls[0]?.envelope.payload).toEqual({
      customerId,
      emailThreadId: threadId,
      status: 'Archived',
      unreadCount: 0,
      messageCount: 3,
      actorUserId: actorId,
    });
  });

  it('ignores a payload without the thread counters', () => {
    expect(
      parseCorrespondenceThreadStateChanged({ CustomerId: customerId, EmailThreadId: threadId }).success,
    ).toBe(false);
  });
});
