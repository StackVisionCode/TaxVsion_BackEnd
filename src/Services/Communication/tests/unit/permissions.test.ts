import { randomUUID } from 'node:crypto';
import { describe, expect, it } from 'vitest';
import {
  checkPermission,
  CommunicationPermissions,
  permissionCheckHttpStatus,
} from '../../src/domain/shared/permissions.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';

describe('checkPermission', () => {
  it('an inactive user is denied even with the permission', async () => {
    const userId = randomUUID();
    const projection = createFakeProjectionRepository([
      fakeSnapshot({ userId, isActive: false, permissions: [CommunicationPermissions.NotificationRead] }),
    ]);

    const result = await checkPermission(
      { userId, tenantId: 'tenant-fake', actorType: 'TenantEmployee', permissionVersion: 1 },
      CommunicationPermissions.NotificationRead,
      projection,
    );

    expect(result).toMatchObject({ allowed: false, code: 'Auth.UserInactive' });
    if (!result.allowed) expect(permissionCheckHttpStatus(result)).toBe(403);
  });

  it('PlatformAdmin keeps its bypass', async () => {
    const userId = randomUUID();
    const projection = createFakeProjectionRepository([fakeSnapshot({ userId, isActive: false })]);

    const result = await checkPermission(
      { userId, tenantId: 'tenant-fake', actorType: 'PlatformAdmin', permissionVersion: 1 },
      CommunicationPermissions.NotificationRead,
      projection,
    );

    expect(result.allowed).toBe(true);
  });
});
