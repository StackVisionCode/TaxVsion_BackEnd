import { describe, expect, it, vi } from 'vitest';
import {
  decodeDirectoryCursor,
  encodeDirectoryCursor,
  pageCustomerDirectory,
  pageStaffDirectory,
} from '../../src/application/use-cases/page-chat-directory.js';
import type { UserDirectoryEntrySnapshot } from '../../src/application/ports/user-directory-repository.js';
import type { CustomerDirectoryEntrySnapshot } from '../../src/application/ports/customer-directory-repository.js';
import { createFakeProjectionRepository, fakeSnapshot } from '../helpers/fake-user-permissions-projection-repository.js';

/** F8c (T-14) — páginas del rail con el mismo criterio de "se puede chatear" que start_direct. */
describe('page-chat-directory', () => {
  const tenantId = 't1';
  const settings = (over: Record<string, unknown> = {}) => ({
    get: async () => ({ tenantId, chatEnabled: true, employeeToEmployeeChatEnabled: true, restrictCustomerChatToAssignedPreparer: false, screenshotsEnabled: true, messageRetentionDays: 365, ...over }),
  });
  const staffRow = (userId: string, displayName: string, actorType = 'TenantEmployee'): UserDirectoryEntrySnapshot => ({
    userId, tenantId, displayName, email: `${userId}@x.test`, isActive: true, actorType, avatarFileId: null, updatedAtUtc: new Date(0),
  });
  const customerRow = (customerId: string, displayName: string): CustomerDirectoryEntrySnapshot => ({
    customerId, tenantId, displayName, email: `${customerId}@x.test`, isActive: true, avatarFileId: null, updatedAtUtc: new Date(0),
  });

  it('cursor ida y vuelta; uno manipulado es un error de cursor', () => {
    const raw = encodeDirectoryCursor({ displayName: 'Ana', id: 'u1' });
    const back = decodeDirectoryCursor(raw);
    expect(back.isSuccess && back.value).toEqual({ displayName: 'Ana', id: 'u1' });
    const bad = decodeDirectoryCursor('not-a-cursor');
    expect(bad.isSuccess ? 'ok' : bad.error.code).toBe('Directory.InvalidCursor');
  });

  it('staff: pide una fila de más para saber si hay otra página y marca empleado↔empleado apagado', async () => {
    const pageStaff = vi.fn(async () => [staffRow('a', 'Ana'), staffRow('b', 'Beto', 'TenantAdmin'), staffRow('c', 'Caro')]);
    const r = await pageStaffDirectory(
      { tenantId, actor: { userId: 'me', actorType: 'TenantEmployee' }, query: '  ', limit: 2 },
      { staffDirectory: { pageStaff }, settings: settings({ employeeToEmployeeChatEnabled: false }) },
    );
    expect(pageStaff).toHaveBeenCalledWith(expect.objectContaining({ query: null, take: 3, excludeUserId: 'me', after: null }));
    if (!r.isSuccess) throw new Error(r.error.code);
    expect(r.value.items.map((i) => [i.displayName, i.chatBlockedReason])).toEqual([
      ['Ana', 'EmployeeToEmployeeDisabled'],
      ['Beto', null],
    ]);
    expect(decodeDirectoryCursor(r.value.nextCursor)).toMatchObject({ isSuccess: true, value: { displayName: 'Beto', id: 'b' } });
  });

  it('un Tenant Admin no tiene el bloqueo empleado↔empleado; la última página no trae cursor', async () => {
    const r = await pageStaffDirectory(
      { tenantId, actor: { userId: 'me', actorType: 'TenantAdmin' } },
      { staffDirectory: { pageStaff: async () => [staffRow('a', 'Ana')] }, settings: settings({ employeeToEmployeeChatEnabled: false }) },
    );
    expect(r.isSuccess && r.value).toEqual({ items: [expect.objectContaining({ chatBlockedReason: null })], nextCursor: null });
  });

  it('clientes: con visibilidad por asignación pasa el ABAC a la consulta; sin portal = bloqueado', async () => {
    const pageCustomers = vi.fn(async () => [customerRow('c1', 'Diego'), customerRow('c2', 'Eva')]);
    const r = await pageCustomerDirectory(
      { tenantId, actor: { userId: 'prep', actorType: 'TenantEmployee' } },
      {
        customerDirectoryPager: { pageCustomers },
        customerPortalAccounts: { findActiveByCustomerIds: async () => [{ customerId: 'c1', tenantId, userId: 'portal-1', isActive: true }] },
        userPermissions: createFakeProjectionRepository([fakeSnapshot({ userId: 'prep' })]),
        settings: settings({ restrictCustomerChatToAssignedPreparer: true }),
      },
    );
    expect(pageCustomers).toHaveBeenCalledWith(expect.objectContaining({ assignedToUserId: 'prep', take: 21 }));
    if (!r.isSuccess) throw new Error(r.error.code);
    expect(r.value.items.map((i) => [i.displayName, i.portalUserId, i.chatBlockedReason])).toEqual([
      ['Diego', 'portal-1', null],
      ['Eva', null, 'NoPortalAccount'],
    ]);
  });

  it('quien ve todos los clientes no lleva filtro de asignación', async () => {
    const pageCustomers = vi.fn(async () => []);
    await pageCustomerDirectory(
      { tenantId, actor: { userId: 'boss', actorType: 'TenantEmployee' } },
      {
        customerDirectoryPager: { pageCustomers },
        customerPortalAccounts: { findActiveByCustomerIds: async () => [] },
        userPermissions: createFakeProjectionRepository([fakeSnapshot({ userId: 'boss', permissions: ['customers.view_all'] })]),
        settings: settings(),
        assignmentVisibilityEnabled: true,
      },
    );
    expect(pageCustomers).toHaveBeenCalledWith(expect.objectContaining({ assignedToUserId: null }));
  });
});
