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
import type { CustomerAssignmentProjectionRepository } from '../../src/application/ports/customer-assignment-projection-repository.js';
import type { UserPermissionsProjectionRepository } from '../../src/application/ports/user-permissions-projection-repository.js';
import type { TenantSettingsProvider } from '../../src/application/ports/tenant-settings-provider.js';

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
    async markActiveByUserId(): Promise<void> {},
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

// Deps de visibilidad (P2) — mínimos + cast: el use-case solo llama getAssignedCustomerIds/findByUserId/get.
function fakeAssignments(byUser: Record<string, string[]>): CustomerAssignmentProjectionRepository {
  return {
    async getAssignedCustomerIds(_tenantId: string, userId: string): Promise<string[]> {
      return byUser[userId] ?? [];
    },
  } as unknown as CustomerAssignmentProjectionRepository;
}

function fakeUserPermissions(byUser: Record<string, string[]>): UserPermissionsProjectionRepository {
  return {
    async findByUserId(userId: string) {
      const perms = byUser[userId];
      if (!perms) return null;
      return {
        userId,
        tenantId: 'tenant-1',
        permissions: perms,
        permissionVersion: 1,
        roleIds: [],
        actorType: 'TenantEmployee',
        isActive: true,
        updatedAtUtc: new Date(),
      };
    },
  } as unknown as UserPermissionsProjectionRepository;
}

function fakeSettings(restrict: boolean): TenantSettingsProvider {
  return {
    async get(tenantId: string) {
      return {
        tenantId,
        chatEnabled: true,
        employeeToEmployeeChatEnabled: true,
        restrictCustomerChatToAssignedPreparer: restrict,
        screenshotsEnabled: true,
        internalGroupsEnabled: true,
        messageRetentionDays: 365,
      };
    },
  } as unknown as TenantSettingsProvider;
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

// Deps por defecto: ambos flags OFF, sin permisos, sin asignaciones (no restringe).
// `restrict` = setting por-tenant; `globalFlag` = flag global de despliegue (P2).
function baseDeps(overrides: {
  directory: CustomerDirectoryRepository;
  portalAccounts: CustomerPortalAccountRepository;
  restrict?: boolean;
  globalFlag?: boolean;
  perms?: Record<string, string[]>;
  assignments?: Record<string, string[]>;
}) {
  return {
    customerDirectory: overrides.directory,
    customerPortalAccounts: overrides.portalAccounts,
    customerAssignments: fakeAssignments(overrides.assignments ?? {}),
    userPermissions: fakeUserPermissions(overrides.perms ?? {}),
    settings: fakeSettings(overrides.restrict ?? false),
    assignmentVisibilityEnabled: overrides.globalFlag ?? false,
  };
}

const ACTOR = { actorUserId: 'actor-1', actorType: 'TenantEmployee' };

describe('searchCustomerDirectory', () => {
  it('attaches the portal userId when the customer has an active portal account', async () => {
    const withPortal = u();
    const userId = u();
    const deps = baseDeps({
      directory: fakeDirectory([entry(withPortal, 'Manuel')]),
      portalAccounts: fakePortalAccounts([{ customerId: withPortal, tenantId: 'tenant-1', userId, isActive: true }]),
    });

    const result = (await searchCustomerDirectory({ tenantId: 'tenant-1', query: 'Man', ...ACTOR }, deps))[0]!;

    expect(result.customerId).toBe(withPortal);
    expect(result.portalUserId).toBe(userId);
  });

  it('returns portalUserId null for a customer without a portal account (not chateable yet)', async () => {
    const noPortal = u();
    const deps = baseDeps({
      directory: fakeDirectory([entry(noPortal, 'Sofia')]),
      portalAccounts: fakePortalAccounts([]),
    });

    const result = (await searchCustomerDirectory({ tenantId: 'tenant-1', query: 'Sof', ...ACTOR }, deps))[0]!;

    expect(result.portalUserId).toBeNull();
  });

  it('resolves a mixed result in a single batch lookup', async () => {
    const withPortal = u();
    const noPortal = u();
    const userId = u();
    const deps = baseDeps({
      directory: fakeDirectory([entry(withPortal, 'A'), entry(noPortal, 'B')]),
      portalAccounts: fakePortalAccounts([{ customerId: withPortal, tenantId: 'tenant-1', userId, isActive: true }]),
    });

    const results = await searchCustomerDirectory({ tenantId: 'tenant-1', query: '', ...ACTOR }, deps);

    expect(results.find((r) => r.customerId === withPortal)?.portalUserId).toBe(userId);
    expect(results.find((r) => r.customerId === noPortal)?.portalUserId).toBeNull();
  });

  it('returns an empty array without touching portal accounts when nothing matches', async () => {
    const deps = baseDeps({ directory: fakeDirectory([]), portalAccounts: fakePortalAccounts([]) });

    const results = await searchCustomerDirectory({ tenantId: 'tenant-1', query: 'zzz', ...ACTOR }, deps);

    expect(results).toEqual([]);
  });

  // ---- Visibilidad por asignación (P2) ----

  it('sin restricción (flag OFF) devuelve TODOS aunque el actor no esté asignado', async () => {
    const c1 = u();
    const c2 = u();
    const deps = baseDeps({
      directory: fakeDirectory([entry(c1, 'A'), entry(c2, 'B')]),
      portalAccounts: fakePortalAccounts([]),
      restrict: false,
    });

    const results = await searchCustomerDirectory({ tenantId: 'tenant-1', query: '', ...ACTOR }, deps);
    expect(results).toHaveLength(2);
  });

  it('con flag ON y actor sin view_all: solo devuelve sus clientes asignados', async () => {
    const assigned = u();
    const notAssigned = u();
    const deps = baseDeps({
      directory: fakeDirectory([entry(assigned, 'Asignado'), entry(notAssigned, 'Otro')]),
      portalAccounts: fakePortalAccounts([]),
      restrict: true,
      assignments: { 'actor-1': [assigned] },
    });

    const results = await searchCustomerDirectory({ tenantId: 'tenant-1', query: '', ...ACTOR }, deps);
    expect(results).toHaveLength(1);
    expect(results[0]!.customerId).toBe(assigned);
  });

  it('con flag GLOBAL ON (tenant setting OFF) y actor sin view_all: solo asignados', async () => {
    const assigned = u();
    const notAssigned = u();
    const deps = baseDeps({
      directory: fakeDirectory([entry(assigned, 'Asignado'), entry(notAssigned, 'Otro')]),
      portalAccounts: fakePortalAccounts([]),
      restrict: false,
      globalFlag: true,
      assignments: { 'actor-1': [assigned] },
    });

    const results = await searchCustomerDirectory({ tenantId: 'tenant-1', query: '', ...ACTOR }, deps);
    expect(results).toHaveLength(1);
    expect(results[0]!.customerId).toBe(assigned);
  });

  it('con flag ON y actor con customers.view_all: devuelve TODOS (bypass admin)', async () => {
    const c1 = u();
    const c2 = u();
    const deps = baseDeps({
      directory: fakeDirectory([entry(c1, 'A'), entry(c2, 'B')]),
      portalAccounts: fakePortalAccounts([]),
      restrict: true,
      perms: { 'actor-1': ['customers.view_all'] },
      assignments: {},
    });

    const results = await searchCustomerDirectory({ tenantId: 'tenant-1', query: '', ...ACTOR }, deps);
    expect(results).toHaveLength(2);
  });

  it('con flag ON y actor sin clientes asignados: devuelve vacío', async () => {
    const c1 = u();
    const deps = baseDeps({
      directory: fakeDirectory([entry(c1, 'A')]),
      portalAccounts: fakePortalAccounts([]),
      restrict: true,
      assignments: {},
    });

    const results = await searchCustomerDirectory({ tenantId: 'tenant-1', query: '', ...ACTOR }, deps);
    expect(results).toEqual([]);
  });
});
