import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { searchCustomerDirectory } from '../../src/application/use-cases/search-customer-directory.js';
import type {
  CustomerDirectoryRepository,
  CustomerDirectoryEntrySnapshot,
} from '../../src/application/ports/customer-directory-repository.js';
import type {
  CustomerPortalAccountRepository,
  CustomerPortalAccountSnapshot,
} from '../../src/application/ports/customer-portal-account-repository.js';

function u(): string {
  return randomUUID();
}

function fakeDirectory(entries: CustomerDirectoryEntrySnapshot[]): CustomerDirectoryRepository {
  return {
    async upsert(): Promise<void> {},
    async findByCustomerId(): Promise<CustomerDirectoryEntrySnapshot | null> {
      return null;
    },
    async markInactive(): Promise<void> {},
    async searchByDisplayNameOrEmail(): Promise<CustomerDirectoryEntrySnapshot[]> {
      return entries;
    },
  };
}

function fakePortalAccounts(active: CustomerPortalAccountSnapshot[]): CustomerPortalAccountRepository {
  return {
    async upsert(): Promise<void> {},
    async markInactiveByUserId(): Promise<void> {},
    async findActiveByCustomerId(): Promise<CustomerPortalAccountSnapshot | null> {
      return null;
    },
    async findActiveByCustomerIds(customerIds: readonly string[]): Promise<CustomerPortalAccountSnapshot[]> {
      return active.filter((a) => customerIds.includes(a.customerId));
    },
    async findActiveByUserId(): Promise<CustomerPortalAccountSnapshot | null> {
      return null;
    },
  };
}

function entry(customerId: string, name: string): CustomerDirectoryEntrySnapshot {
  return {
    customerId,
    tenantId: 'tenant-1',
    displayName: name,
    email: `${name}@example.com`,
    isActive: true,
    updatedAtUtc: new Date(),
  };
}

describe('searchCustomerDirectory', () => {
  it('attaches the portal userId when the customer has an active portal account', async () => {
    const withPortal = u();
    const userId = u();
    const deps = {
      customerDirectory: fakeDirectory([entry(withPortal, 'Manuel')]),
      customerPortalAccounts: fakePortalAccounts([
        { customerId: withPortal, tenantId: 'tenant-1', userId, isActive: true },
      ]),
    };

    const result = (await searchCustomerDirectory({ tenantId: 'tenant-1', query: 'Man' }, deps))[0]!;

    expect(result.customerId).toBe(withPortal);
    expect(result.portalUserId).toBe(userId);
  });

  it('returns portalUserId null for a customer without a portal account (not chateable yet)', async () => {
    const noPortal = u();
    const deps = {
      customerDirectory: fakeDirectory([entry(noPortal, 'Sofia')]),
      customerPortalAccounts: fakePortalAccounts([]),
    };

    const result = (await searchCustomerDirectory({ tenantId: 'tenant-1', query: 'Sof' }, deps))[0]!;

    expect(result.portalUserId).toBeNull();
  });

  it('resolves a mixed result in a single batch lookup', async () => {
    const withPortal = u();
    const noPortal = u();
    const userId = u();
    const deps = {
      customerDirectory: fakeDirectory([entry(withPortal, 'A'), entry(noPortal, 'B')]),
      customerPortalAccounts: fakePortalAccounts([
        { customerId: withPortal, tenantId: 'tenant-1', userId, isActive: true },
      ]),
    };

    const results = await searchCustomerDirectory({ tenantId: 'tenant-1', query: '' }, deps);

    expect(results.find((r) => r.customerId === withPortal)?.portalUserId).toBe(userId);
    expect(results.find((r) => r.customerId === noPortal)?.portalUserId).toBeNull();
  });

  it('returns an empty array without touching portal accounts when nothing matches', async () => {
    const deps = {
      customerDirectory: fakeDirectory([]),
      customerPortalAccounts: fakePortalAccounts([]),
    };

    const results = await searchCustomerDirectory({ tenantId: 'tenant-1', query: 'zzz' }, deps);

    expect(results).toEqual([]);
  });
});
