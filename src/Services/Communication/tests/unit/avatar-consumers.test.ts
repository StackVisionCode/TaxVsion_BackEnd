import { describe, expect, it, vi } from 'vitest';
import { bindAuthConsumers } from '../../src/application/event-handlers/auth-consumers.js';
import { bindCustomerConsumers } from '../../src/application/event-handlers/customer-consumers.js';
import {
  emitUserProfileChanged,
  readOptionalGuid,
} from '../../src/application/event-handlers/user-profile-realtime.js';
import { SocketRealtimeEmitter } from '../../src/infrastructure/socket/socket-realtime-emitter.js';
import type { CommunicationIoServer } from '../../src/infrastructure/socket/build-io.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type {
  UserDirectoryEntrySnapshot,
  UserDirectoryRepository,
} from '../../src/application/ports/user-directory-repository.js';
import type {
  CustomerDirectoryEntrySnapshot,
  CustomerDirectoryRepository,
} from '../../src/application/ports/customer-directory-repository.js';
import type { CustomerPortalAccountRepository } from '../../src/application/ports/customer-portal-account-repository.js';
import type { UserPermissionsProjectionRepository } from '../../src/application/ports/user-permissions-projection-repository.js';
import type { RolePermissionsProjectionRepository } from '../../src/application/ports/role-permissions-projection-repository.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { CustomerPreparerAssignmentRepository } from '../../src/application/ports/customer-preparer-assignment-repository.js';
import type { CustomerAssignmentProjectionRepository } from '../../src/application/ports/customer-assignment-projection-repository.js';

/**
 * F5 avatares — los consumers guardan la foto en los directorios y avisan `user.profile.changed`.
 * Payloads con los nombres reales de los eventos .NET (camelCase de Wolverine; PascalCase tolerado).
 */
const TENANT = 'tenant-1';
const USER = '11111111-1111-1111-1111-111111111111';
const PORTAL_USER = '22222222-2222-2222-2222-222222222222';
const CUSTOMER = '33333333-3333-3333-3333-333333333333';
const FILE_A = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
const FILE_B = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';

function fakeEmitter(): RealtimeEmitter {
  return {
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToUser: vi.fn(),
    emitToTenant: vi.fn(),
    emitToTenantStaff: vi.fn(),
    emitToTenantMembers: vi.fn(),
    emitBroadcast: vi.fn(),
  };
}

function envelope(eventType: string, payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType,
    tenantId: TENANT,
    correlationId: 'corr-1',
    occurredOnUtc: '',
    payload,
  };
}

function userEntry(overrides: Partial<UserDirectoryEntrySnapshot> = {}): UserDirectoryEntrySnapshot {
  return {
    userId: USER,
    tenantId: TENANT,
    displayName: 'Old Name',
    email: 'ana@x.test',
    isActive: true,
    actorType: 'TenantEmployee',
    avatarFileId: FILE_A,
    updatedAtUtc: new Date(),
    ...overrides,
  };
}

function profileChangedCalls(emitter: RealtimeEmitter, method: 'emitToTenantStaff' | 'emitToUser') {
  return vi
    .mocked(emitter[method])
    .mock.calls.map((c) => c[0] as { event: string; userId?: string; envelope: { payload: unknown } })
    .filter((c) => c.event === 'user.profile.changed');
}

function setupAuth(existing: UserDirectoryEntrySnapshot | null) {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const userDirectory = {
    upsert: vi.fn(),
    findByUserId: vi.fn().mockResolvedValue(existing),
    markInactive: vi.fn(),
    markActive: vi.fn(),
    searchByDisplayNameOrEmail: vi.fn(),
  } as unknown as UserDirectoryRepository;
  const emitter = fakeEmitter();
  bindAuthConsumers((type, handler) => handlers.set(type, handler), {
    userPermissions: {} as UserPermissionsProjectionRepository,
    rolePermissions: {} as RolePermissionsProjectionRepository,
    customerPortalAccounts: {} as CustomerPortalAccountRepository,
    userDirectory,
    emitter,
  });
  const run = (payload: Record<string, unknown>) =>
    handlers.get('auth.user.profile_updated.v1')!(envelope('auth.user.profile_updated.v1', payload));
  return { run, userDirectory, emitter };
}

describe('auth.user.profile_updated.v1 — nombre y foto', () => {
  it('guarda nombre y foto nueva y avisa al staff y al propio usuario', async () => {
    const { run, userDirectory, emitter } = setupAuth(userEntry());

    await run({ userId: USER, name: 'Ana', lastName: 'Lopez', avatarFileId: FILE_B.toUpperCase() });

    expect(userDirectory.upsert).toHaveBeenCalledWith({
      userId: USER,
      tenantId: TENANT,
      displayName: 'Ana Lopez',
      email: 'ana@x.test',
      isActive: true,
      avatarFileId: FILE_B,
    });
    const expected = { userId: USER, displayName: 'Ana Lopez', avatarFileId: FILE_B };
    expect(profileChangedCalls(emitter, 'emitToTenantStaff').map((c) => c.envelope.payload)).toEqual([
      expected,
    ]);
    const toUser = profileChangedCalls(emitter, 'emitToUser');
    expect(toUser.map((c) => [c.userId, c.envelope.payload])).toEqual([[USER, expected]]);
    // Nunca al tenant entero (portal e invitados).
    expect(emitter.emitToTenant).not.toHaveBeenCalled();
    expect(emitter.emitToTenantMembers).not.toHaveBeenCalled();
  });

  it('null explicito quita la foto', async () => {
    const { run, userDirectory, emitter } = setupAuth(userEntry());

    await run({ UserId: USER, Name: 'Ana', LastName: 'Lopez', AvatarFileId: null });

    expect(vi.mocked(userDirectory.upsert).mock.calls[0]![0]).toMatchObject({ avatarFileId: null });
    expect(profileChangedCalls(emitter, 'emitToTenantStaff')[0]!.envelope.payload).toMatchObject({
      avatarFileId: null,
    });
  });

  it('publicador viejo sin la clave: no toca la foto guardada y anuncia la que ya habia', async () => {
    const { run, userDirectory, emitter } = setupAuth(userEntry());

    await run({ userId: USER, name: 'Ana', lastName: 'Lopez' });

    expect('avatarFileId' in vi.mocked(userDirectory.upsert).mock.calls[0]![0]).toBe(false);
    expect(profileChangedCalls(emitter, 'emitToTenantStaff')[0]!.envelope.payload).toMatchObject({
      avatarFileId: FILE_A,
    });
  });

  it('sin fila en el directorio: no crea una incompleta, pero avisa si el evento trae la foto', async () => {
    const { run, userDirectory, emitter } = setupAuth(null);

    await run({ userId: USER, name: 'Ana', lastName: 'Lopez', avatarFileId: FILE_B });

    expect(userDirectory.upsert).not.toHaveBeenCalled();
    expect(profileChangedCalls(emitter, 'emitToTenantStaff')).toHaveLength(1);
  });

  it('sin fila y sin foto en el evento: nada que anunciar', async () => {
    const { run, userDirectory, emitter } = setupAuth(null);

    await run({ userId: USER, name: 'Ana', lastName: 'Lopez' });

    expect(userDirectory.upsert).not.toHaveBeenCalled();
    expect(profileChangedCalls(emitter, 'emitToTenantStaff')).toHaveLength(0);
  });
});

function customerEntry(
  overrides: Partial<CustomerDirectoryEntrySnapshot> = {},
): CustomerDirectoryEntrySnapshot {
  return {
    customerId: CUSTOMER,
    tenantId: TENANT,
    displayName: 'Cliente SA',
    email: 'c@x.test',
    isActive: true,
    avatarFileId: null,
    updatedAtUtc: new Date(),
    ...overrides,
  };
}

function setupCustomer(existing: CustomerDirectoryEntrySnapshot | null, portal: boolean) {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const customerDirectory = {
    upsert: vi.fn(),
    findByCustomerId: vi.fn().mockResolvedValue(existing),
    markInactive: vi.fn(),
    clearAvatar: vi.fn(),
  } as unknown as CustomerDirectoryRepository;
  const customerPortalAccounts = {
    findActiveByCustomerId: vi
      .fn()
      .mockResolvedValue(
        portal ? { customerId: CUSTOMER, tenantId: TENANT, userId: PORTAL_USER, isActive: true } : null,
      ),
  } as unknown as CustomerPortalAccountRepository;
  const userDirectory = {
    findByUserId: vi.fn().mockResolvedValue(userEntry({ userId: PORTAL_USER, displayName: 'Carla Ruiz' })),
  } as unknown as UserDirectoryRepository;
  const emitter = fakeEmitter();
  bindCustomerConsumers((type, handler) => handlers.set(type, handler), {
    notifications: {} as NotificationRepository,
    customerPreparerAssignments: {} as CustomerPreparerAssignmentRepository,
    customerAssignments: {} as CustomerAssignmentProjectionRepository,
    inAppSource: 'Communication',
    customerDirectory,
    customerPortalAccounts,
    userDirectory,
    emitter,
  });
  const run = (type: string, payload: Record<string, unknown>) =>
    handlers.get(type)!(envelope(type, payload));
  return { run, customerDirectory, emitter };
}

const UPDATED = { customerId: CUSTOMER, displayName: 'Cliente SA', primaryEmail: 'c@x.test' };

describe('customer.* — foto del cliente', () => {
  it('created guarda la foto', async () => {
    const { run, customerDirectory } = setupCustomer(null, false);

    await run('customer.created.v1', { ...UPDATED, avatarFileId: FILE_A });

    expect(vi.mocked(customerDirectory.upsert).mock.calls[0]![0]).toMatchObject({ avatarFileId: FILE_A });
  });

  it('updated con foto nueva: guarda, mantiene customer.changed y avisa por el usuario del portal', async () => {
    const { run, customerDirectory, emitter } = setupCustomer(customerEntry(), true);

    await run('customer.updated.v1', { ...UPDATED, avatarFileId: FILE_B });

    expect(vi.mocked(customerDirectory.upsert).mock.calls[0]![0]).toMatchObject({ avatarFileId: FILE_B });
    const staffEvents = vi
      .mocked(emitter.emitToTenantStaff)
      .mock.calls.map((c) => (c[0] as { event: string }).event);
    expect(staffEvents).toEqual(['customer.changed', 'user.profile.changed']);
    // El nombre es el de Auth (el que muestra el chat), no el de la ficha.
    expect(profileChangedCalls(emitter, 'emitToUser').map((c) => [c.userId, c.envelope.payload])).toEqual([
      [PORTAL_USER, { userId: PORTAL_USER, displayName: 'Carla Ruiz', avatarFileId: FILE_B }],
    ]);
  });

  it('updated con la misma foto no anuncia cambio de perfil', async () => {
    const { run, emitter } = setupCustomer(customerEntry({ avatarFileId: FILE_A }), true);

    await run('customer.updated.v1', { ...UPDATED, AvatarFileId: FILE_A });

    expect(profileChangedCalls(emitter, 'emitToTenantStaff')).toHaveLength(0);
  });

  it('updated sin cuenta de portal: solo customer.changed', async () => {
    const { run, emitter } = setupCustomer(customerEntry(), false);

    await run('customer.updated.v1', { ...UPDATED, avatarFileId: FILE_B });

    expect(profileChangedCalls(emitter, 'emitToTenantStaff')).toHaveLength(0);
    expect(emitter.emitToTenantStaff).toHaveBeenCalledTimes(1);
  });

  it('updated de un publicador viejo (sin la clave) no toca la foto', async () => {
    const { run, customerDirectory, emitter } = setupCustomer(customerEntry({ avatarFileId: FILE_A }), true);

    await run('customer.updated.v1', UPDATED);

    expect('avatarFileId' in vi.mocked(customerDirectory.upsert).mock.calls[0]![0]).toBe(false);
    expect(profileChangedCalls(emitter, 'emitToTenantStaff')).toHaveLength(0);
  });

  it('archived limpia la foto y avisa al portal si tenia una', async () => {
    const { run, customerDirectory, emitter } = setupCustomer(customerEntry({ avatarFileId: FILE_A }), true);

    await run('customer.archived.v1', { customerId: CUSTOMER });

    expect(customerDirectory.markInactive).toHaveBeenCalledWith(CUSTOMER);
    expect(customerDirectory.clearAvatar).toHaveBeenCalledWith(CUSTOMER);
    expect(profileChangedCalls(emitter, 'emitToTenantStaff')[0]!.envelope.payload).toEqual({
      userId: PORTAL_USER,
      displayName: 'Carla Ruiz',
      avatarFileId: null,
    });
  });

  it('archived sin foto previa no anuncia cambio de perfil', async () => {
    const { run, customerDirectory, emitter } = setupCustomer(customerEntry(), true);

    await run('customer.archived.v1', { customerId: CUSTOMER });

    expect(customerDirectory.clearAvatar).toHaveBeenCalledWith(CUSTOMER);
    expect(profileChangedCalls(emitter, 'emitToTenantStaff')).toHaveLength(0);
  });
});

describe('user.profile.changed — rooms del socket', () => {
  it('va al room de staff del tenant y al room del usuario, nunca al tenant entero', () => {
    const emits: { room: string; event: string; envelope: unknown }[] = [];
    const io = {
      to: (room: string) => ({
        emit: (event: string, envelope: unknown) => emits.push({ room, event, envelope }),
      }),
      emit: vi.fn(),
    } as unknown as CommunicationIoServer;

    emitUserProfileChanged(new SocketRealtimeEmitter(io), TENANT, {
      userId: USER,
      displayName: 'Ana Lopez',
      avatarFileId: FILE_A,
    });

    expect(emits.map((e) => [e.room, e.event])).toEqual([
      [`t:${TENANT}:staff`, 'user.profile.changed'],
      [`t:${TENANT}:u:${USER}`, 'user.profile.changed'],
    ]);
    expect((emits[0]!.envelope as { payload: unknown }).payload).toEqual({
      userId: USER,
      displayName: 'Ana Lopez',
      avatarFileId: FILE_A,
    });
  });
});

describe('readOptionalGuid', () => {
  it('distingue ausente, null y valor; Guid.Empty cuenta como sin foto', () => {
    expect(readOptionalGuid({}, 'avatarFileId', 'AvatarFileId')).toBeUndefined();
    expect(readOptionalGuid({ avatarFileId: null }, 'avatarFileId')).toBeNull();
    expect(readOptionalGuid({ AvatarFileId: FILE_A.toUpperCase() }, 'avatarFileId', 'AvatarFileId')).toBe(
      FILE_A,
    );
    expect(
      readOptionalGuid({ avatarFileId: '00000000-0000-0000-0000-000000000000' }, 'avatarFileId'),
    ).toBeNull();
  });
});
