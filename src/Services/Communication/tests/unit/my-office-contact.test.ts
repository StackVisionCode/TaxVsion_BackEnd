import { describe, it, expect, vi } from 'vitest';
import { getMyOfficeContact } from '../../src/application/use-cases/get-my-office-contact.js';
import { startDirectConversation } from '../../src/application/use-cases/start-direct-conversation.js';

const principal = { tenantId: 'tenant-a', userId: 'portal-user', actorType: 'CustomerPortal' };

type Staff = { userId: string; tenantId: string; displayName: string; isActive: boolean; actorType: string };
const staff = (
  userId: string,
  displayName: string,
  actorType = 'TenantEmployee',
  extra: Partial<Staff> = {},
): Staff => ({ userId, tenantId: 'tenant-a', displayName, isActive: true, actorType, ...extra });

// Oficina de prueba: Carlos (admin, principal y asignado), Andres y Diana asignados, Zoe y Bob admins sin asignar.
const directory: Staff[] = [
  staff('carlos', 'Carlos Castillo', 'TenantAdmin'),
  staff('andres', 'Andres Hernandez'),
  staff('diana', 'Diana Admin', 'TenantAdmin'),
  staff('zoe', 'Zoe Office', 'TenantAdmin'),
  staff('bob', 'Bob Office', 'TenantAdmin'),
  staff('gone', 'Gone Admin', 'TenantAdmin', { isActive: false }),
  staff('platform', 'Platform Admin', 'PlatformAdmin'),
  staff('foreign', 'Foreign Staff', 'TenantEmployee', { tenantId: 'tenant-b' }),
  staff('other-client', 'Other Client', 'CustomerPortal'),
];

function setup(opts: { chatEnabled?: boolean; restrict?: boolean; visibility?: boolean } = {}) {
  const assigned = ['carlos', 'andres', 'diana', 'foreign', 'other-client', 'platform'];
  const byId = new Map(directory.map((s) => [s.userId, s]));
  const photos: Record<string, string> = { carlos: 'avatar-carlos', zoe: 'avatar-zoe' };
  return {
    customerPortalAccounts: {
      findActiveByUserId: vi.fn().mockResolvedValue({
        userId: 'portal-user',
        customerId: 'customer-a',
        tenantId: 'tenant-a',
        isActive: true,
      }),
    },
    customerPreparerAssignments: {
      findByCustomerId: vi
        .fn()
        .mockResolvedValue({ tenantId: 'tenant-a', customerId: 'customer-a', preparerUserId: 'carlos' }),
    },
    customerAssignments: {
      getAssignedUserIds: vi.fn().mockResolvedValue(assigned),
      isAssigned: vi.fn(async (_t: string, _c: string, userId: string) => assigned.includes(userId)),
    },
    userDirectory: {
      findByUserId: vi.fn(async (id: string) => byId.get(id) ?? null),
      findByUserIds: vi.fn(async (ids: readonly string[]) => ids.flatMap((id) => byId.get(id) ?? [])),
      findActiveAdmins: vi.fn(async (tenantId: string) =>
        directory.filter((s) => s.tenantId === tenantId && s.isActive && s.actorType === 'TenantAdmin'),
      ),
    },
    // Zoe y Carlos ven a todos los clientes; Bob no.
    userPermissions: {
      findByUserId: vi.fn(async (id: string) =>
        id === 'zoe' || id === 'carlos' ? { permissions: ['customers.view_all'] } : { permissions: [] },
      ),
    },
    settings: {
      get: vi.fn().mockResolvedValue({
        tenantId: 'tenant-a',
        chatEnabled: opts.chatEnabled ?? true,
        employeeToEmployeeChatEnabled: true,
        restrictCustomerChatToAssignedPreparer: opts.restrict ?? false,
        screenshotsEnabled: true,
      }),
    },
    assignmentVisibilityEnabled: opts.visibility ?? false,
    avatars: {
      resolve: vi.fn(async () => ({ of: (id: string) => photos[id] ?? null })),
    },
  };
}

describe('portal office contact', () => {
  it('lists the assigned team and the office admins with their photos', async () => {
    const d = setup();
    expect(await getMyOfficeContact(principal, d as never)).toEqual([
      {
        userId: 'carlos',
        displayName: 'Carlos Castillo',
        isPrimaryPreparer: true,
        relation: 'preparer',
        avatarFileId: 'avatar-carlos',
      },
      {
        userId: 'andres',
        displayName: 'Andres Hernandez',
        isPrimaryPreparer: false,
        relation: 'team',
        avatarFileId: null,
      },
      {
        userId: 'diana',
        displayName: 'Diana Admin',
        isPrimaryPreparer: false,
        relation: 'team',
        avatarFileId: null,
      },
      {
        userId: 'bob',
        displayName: 'Bob Office',
        isPrimaryPreparer: false,
        relation: 'office',
        avatarFileId: null,
      },
      {
        userId: 'zoe',
        displayName: 'Zoe Office',
        isPrimaryPreparer: false,
        relation: 'office',
        avatarFileId: 'avatar-zoe',
      },
    ]);
    expect(d.customerAssignments.getAssignedUserIds).toHaveBeenCalledWith('tenant-a', 'customer-a');
    expect(d.userDirectory.findActiveAdmins).toHaveBeenCalledWith('tenant-a');
  });

  it('the primary preparer comes first and nobody appears twice', async () => {
    const d = setup();
    const result = await getMyOfficeContact(principal, d as never);
    const ids = result.map((c) => c.userId);
    expect(ids[0]).toBe('carlos');
    expect(new Set(ids).size).toBe(ids.length);
    expect(result.filter((c) => c.isPrimaryPreparer)).toHaveLength(1);
    // Admin asignado: sale una vez como equipo, no como oficina.
    expect(result.find((c) => c.userId === 'diana')?.relation).toBe('team');
  });

  it('never offers someone the start rule would reject', async () => {
    for (const opts of [{ visibility: true }, { restrict: true }]) {
      const d = setup(opts);
      const offered = await getMyOfficeContact(principal, d as never);
      // Bob es admin pero sin asignar y sin customers.view_all: el gate lo rechaza.
      expect(offered.map((c) => c.userId)).toEqual(['carlos', 'andres', 'diana', 'zoe']);

      const start = (recipientId: string) => {
        const r = directory.find((s) => s.userId === recipientId) ?? staff(recipientId, recipientId);
        return startDirectConversation(
          {
            tenantId: 'tenant-a',
            correlationId: 'corr',
            clientKey: `key-${recipientId}`,
            initiator: { userId: 'portal-user', displayName: 'Client', actorType: 'CustomerPortal' },
            recipient: { userId: r.userId, displayName: r.displayName, actorType: r.actorType },
          },
          {
            ...d,
            conversations: { findByUniquenessKey: vi.fn().mockResolvedValue(null), save: vi.fn() },
            idempotency: {
              tryReserve: vi.fn().mockResolvedValue({ status: 'fresh', token: 't' }),
              commit: vi.fn(),
              release: vi.fn(),
            },
            publisher: { enqueue: vi.fn() },
          } as never,
        );
      };
      for (const c of offered) expect((await start(c.userId)).isSuccess).toBe(true);
      const rejected = await start('bob');
      expect(rejected.isSuccess ? null : rejected.error.code).toBe('Chat.NotAssignedPreparer');
    }
  });

  it('returns nothing when chat is disabled', async () => {
    const d = setup({ chatEnabled: false });
    expect(await getMyOfficeContact(principal, d as never)).toEqual([]);
    expect(d.userDirectory.findActiveAdmins).not.toHaveBeenCalled();
  });

  it('fails closed for non portal actor', async () => {
    const d = setup();
    expect(await getMyOfficeContact({ ...principal, actorType: 'TenantEmployee' }, d as never)).toEqual([]);
    expect(d.customerPortalAccounts.findActiveByUserId).not.toHaveBeenCalled();
  });

  it.each(['missing', 'inactive', 'tenant'])('returns nothing for a %s portal account', async (kind) => {
    const d = setup();
    if (kind === 'missing') d.customerPortalAccounts.findActiveByUserId.mockResolvedValue(null);
    if (kind === 'inactive')
      d.customerPortalAccounts.findActiveByUserId.mockResolvedValue({
        customerId: 'customer-a',
        tenantId: 'tenant-a',
        isActive: false,
      });
    if (kind === 'tenant')
      d.customerPortalAccounts.findActiveByUserId.mockResolvedValue({
        customerId: 'customer-a',
        tenantId: 'tenant-b',
        isActive: true,
      });
    expect(await getMyOfficeContact(principal, d as never)).toEqual([]);
  });

  it('still lists the office when the photo lookup is unavailable', async () => {
    const d = setup();
    const result = await getMyOfficeContact(principal, { ...d, avatars: undefined } as never);
    expect(result.map((c) => c.avatarFileId)).toEqual([null, null, null, null, null]);
  });
});
