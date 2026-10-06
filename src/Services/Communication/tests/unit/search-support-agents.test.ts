import { describe, expect, it, vi } from 'vitest';
import type { UserDirectoryRepository } from '../../src/application/ports/user-directory-repository.js';
import type { UserPermissionsProjectionRepository } from '../../src/application/ports/user-permissions-projection-repository.js';
import { searchSupportAgents } from '../../src/application/use-cases/search-support-agents.js';
import { CommunicationPermissions } from '../../src/domain/shared/permissions.js';

describe('searchSupportAgents', () => {
  it('returns only platform users that can act as support agents', async () => {
    const platformTenantId = '8f58a521-4c25-4d91-9f4e-7ad5df14c001';
    const adminUserId = '11111111-1111-1111-1111-111111111111';
    const supportUserId = '22222222-2222-2222-2222-222222222222';
    const employeeUserId = '33333333-3333-3333-3333-333333333333';

    const userDirectory = {
      searchByDisplayNameOrEmail: vi.fn().mockResolvedValue([
        {
          userId: adminUserId,
          tenantId: platformTenantId,
          displayName: 'Carlos Castillo',
          email: 'carlos@example.com',
          isActive: true,
          actorType: 'PlatformAdmin',
          updatedAtUtc: new Date(),
        },
        {
          userId: supportUserId,
          tenantId: platformTenantId,
          displayName: 'Ana Support',
          email: 'ana@example.com',
          isActive: true,
          actorType: 'TenantEmployee',
          updatedAtUtc: new Date(),
        },
        {
          userId: employeeUserId,
          tenantId: platformTenantId,
          displayName: 'No Support',
          email: 'employee@example.com',
          isActive: true,
          actorType: 'TenantEmployee',
          updatedAtUtc: new Date(),
        },
      ]),
      upsert: vi.fn(),
      findByUserId: vi.fn(),
      findByUserIds: vi.fn(),
      markInactive: vi.fn(),
      markActive: vi.fn(),
    } satisfies UserDirectoryRepository;

    const userPermissions = {
      findByUserId: vi.fn().mockImplementation((userId: string) =>
        Promise.resolve({
          userId,
          tenantId: platformTenantId,
          permissions: userId === supportUserId ? [CommunicationPermissions.SupportAgent] : [],
          permissionVersion: 1,
          roleIds: [],
          actorType: 'TenantEmployee',
          isActive: true,
          updatedAtUtc: new Date(),
        }),
      ),
      upsert: vi.fn(),
      upsertIdentityPreservingPermissions: vi.fn(),
      findActiveByTenantAndPermission: vi.fn(),
      findActiveSupportRecipients: vi.fn(),
      markInactive: vi.fn(),
      markActive: vi.fn(),
      findActiveByTenantAndRoleId: vi.fn(),
    } satisfies UserPermissionsProjectionRepository;

    const result = await searchSupportAgents(
      { agentTenantId: platformTenantId, query: 'a', limit: 10 },
      { userDirectory, userPermissions },
    );

    expect(result.map((agent) => agent.userId)).toEqual([adminUserId, supportUserId]);
    expect(userDirectory.searchByDisplayNameOrEmail).toHaveBeenCalledWith(platformTenantId, 'a', 10);
    expect(userPermissions.findByUserId).toHaveBeenCalledWith(supportUserId);
    expect(userPermissions.findByUserId).toHaveBeenCalledWith(employeeUserId);
    expect(userPermissions.findByUserId).not.toHaveBeenCalledWith(adminUserId);
  });
});
