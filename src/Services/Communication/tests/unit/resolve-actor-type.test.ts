import { describe, expect, it } from 'vitest';
import { resolveActorType } from '../../src/api/socket/handlers/resolve-actor-type.js';
import type { UserDirectoryEntrySnapshot, UserDirectoryRepository } from '../../src/application/ports/user-directory-repository.js';

/**
 * Fase B6 (auditoria del plan de chat tipado) — el MD pedia explicitamente
 * tests de resolveActorType para empleado, admin, cliente con/sin cuenta de
 * portal y guest. `resolveActorType` en si mismo solo consulta
 * UserDirectoryRepository (fuente unica poblada para TODO actor con email,
 * incluidos CustomerPortal — ver docblock del archivo), asi que estos tests
 * cubren esa unica fuente en sus 4 variantes + el fallback documentado.
 */
function entry(overrides: Partial<UserDirectoryEntrySnapshot>): UserDirectoryEntrySnapshot {
  return {
    userId: 'user-1',
    tenantId: 'tenant-1',
    displayName: 'Someone',
    email: 'someone@example.com',
    isActive: true,
    actorType: 'TenantEmployee',
    updatedAtUtc: new Date(),
    ...overrides,
  };
}

function fakeDirectory(result: UserDirectoryEntrySnapshot | null): UserDirectoryRepository {
  return {
    upsert: async () => {},
    findByUserId: async () => result,
    markInactive: async () => {},
    searchByDisplayNameOrEmail: async () => [],
  };
}

describe('resolveActorType', () => {
  it('resolves a TenantEmployee entry', async () => {
    const directory = fakeDirectory(entry({ actorType: 'TenantEmployee' }));
    await expect(resolveActorType(directory, 'user-1')).resolves.toBe('TenantEmployee');
  });

  it('resolves a PlatformAdmin entry', async () => {
    const directory = fakeDirectory(entry({ actorType: 'PlatformAdmin' }));
    await expect(resolveActorType(directory, 'user-1')).resolves.toBe('PlatformAdmin');
  });

  it('resolves a CustomerPortal entry (customer with an active portal account)', async () => {
    const directory = fakeDirectory(entry({ actorType: 'CustomerPortal' }));
    await expect(resolveActorType(directory, 'customer-user-1')).resolves.toBe('CustomerPortal');
  });

  it('resolves a Guest entry', async () => {
    const directory = fakeDirectory(entry({ actorType: 'Guest' }));
    await expect(resolveActorType(directory, 'guest-1')).resolves.toBe('Guest');
  });

  it('falls back to TenantEmployee when the user has no directory entry yet (race with auth-consumers hydration)', async () => {
    const directory = fakeDirectory(null);
    await expect(resolveActorType(directory, 'unknown-user')).resolves.toBe('TenantEmployee');
  });
});
