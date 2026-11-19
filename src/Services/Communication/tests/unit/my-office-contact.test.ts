import { describe, it, expect, vi } from 'vitest';
import { getMyOfficeContact } from '../../src/application/use-cases/get-my-office-contact.js';
const principal = { tenantId: 'tenant-a', userId: 'portal-user', actorType: 'CustomerPortal' };
function setup() {
  return {
    customerPortalAccounts: {
      findActiveByUserId: vi
        .fn()
        .mockResolvedValue({
          userId: 'portal-user',
          customerId: 'customer-a',
          tenantId: 'tenant-a',
          isActive: true,
        }),
    },
    customerPreparerAssignments: {
      findByCustomerId: vi
        .fn()
        .mockResolvedValue({ tenantId: 'tenant-a', customerId: 'customer-a', preparerUserId: 'employee' }),
    },
    userDirectory: {
      findByUserId: vi
        .fn()
        .mockResolvedValue({
          userId: 'employee',
          tenantId: 'tenant-a',
          displayName: 'My preparer',
          isActive: true,
          actorType: 'TenantEmployee',
        }),
    },
  };
}
describe('portal office contact', () => {
  it('only returns own active assigned preparer', async () => {
    const d = setup();
    expect(await getMyOfficeContact(principal, d as never)).toEqual([
      { userId: 'employee', displayName: 'My preparer', isPrimaryPreparer: true },
    ]);
    expect(d.customerPreparerAssignments.findByCustomerId).toHaveBeenCalledWith('tenant-a', 'customer-a');
  });
  it('fails closed for non portal actor', async () => {
    const d = setup();
    expect(await getMyOfficeContact({ ...principal, actorType: 'TenantEmployee' }, d as never)).toEqual([]);
    expect(d.userDirectory.findByUserId).not.toHaveBeenCalled();
  });
  it.each(['portal', 'assignment', 'employee', 'inactive', 'customer'])(
    'rejects %s mismatch',
    async (kind) => {
      const d = setup();
      if (kind === 'portal')
        d.customerPortalAccounts.findActiveByUserId.mockResolvedValue({
          tenantId: 'tenant-b',
          isActive: true,
        });
      if (kind === 'assignment')
        d.customerPreparerAssignments.findByCustomerId.mockResolvedValue({ tenantId: 'tenant-b' });
      if (kind === 'employee')
        d.userDirectory.findByUserId.mockResolvedValue({
          tenantId: 'tenant-b',
          isActive: true,
          actorType: 'TenantEmployee',
        });
      if (kind === 'inactive')
        d.userDirectory.findByUserId.mockResolvedValue({
          tenantId: 'tenant-a',
          isActive: false,
          actorType: 'TenantEmployee',
        });
      if (kind === 'customer')
        d.userDirectory.findByUserId.mockResolvedValue({
          tenantId: 'tenant-a',
          isActive: true,
          actorType: 'CustomerPortal',
        });
      expect(await getMyOfficeContact(principal, d as never)).toEqual([]);
    },
  );
});
