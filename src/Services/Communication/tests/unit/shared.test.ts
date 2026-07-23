import { describe, expect, it } from 'vitest';
import { Result, makeError } from '../../src/domain/shared/result.js';
import { TenantId, UserId } from '../../src/domain/shared/ids.js';
import { checkPermission, CommunicationPermissions } from '../../src/domain/shared/permissions.js';
import { normalizeCorrelationId } from '../../src/domain/shared/correlation.js';
import { createFakeProjectionRepository, fakeSnapshot } from '../helpers/fake-user-permissions-projection-repository.js';

describe('Result', () => {
  it('ok wraps a value and flags success', () => {
    const r = Result.ok(42);
    expect(r.isSuccess).toBe(true);
    if (r.isSuccess) expect(r.value).toBe(42);
  });

  it('fail wraps an error and flags failure', () => {
    const r = Result.fail(makeError('X.Test', 'boom'));
    expect(r.isSuccess).toBe(false);
    if (!r.isSuccess) expect(r.error.code).toBe('X.Test');
  });
});

describe('branded ids', () => {
  it('parses lowercase UUID and rejects invalid input', () => {
    const id = TenantId('8f58a521-4c25-4d91-9f4e-7ad5df14c001');
    expect(id).toBe('8f58a521-4c25-4d91-9f4e-7ad5df14c001');
    expect(() => TenantId('not-a-uuid')).toThrow();
  });

  it('normalizes uppercase to lowercase (kills legacy case bug)', () => {
    const id = UserId('AAAAAAAA-BBBB-CCCC-DDDD-EEEEEEEEEEEE');
    expect(id).toBe('aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee');
  });
});

describe('permissions', () => {
  it('PlatformAdmin bypasses catalog', async () => {
    // Repo vacio a proposito: si el bypass no cortara antes de la proyeccion, esto fallaria cerrado.
    const repo = createFakeProjectionRepository([]);
    const result = await checkPermission(
      { userId: 'u-platform-admin', actorType: 'PlatformAdmin', permissionVersion: 1 },
      CommunicationPermissions.MeetingHost,
      repo,
    );
    expect(result.allowed).toBe(true);
  });

  it('TenantAdmin without the permission in the projection is rejected (RBAC Fase 7.5.9 — ya no lee el claim perm del JWT)', async () => {
    const repo = createFakeProjectionRepository([fakeSnapshot({ userId: 'u-tenant-admin-1', permissions: [] })]);
    const result = await checkPermission(
      { userId: 'u-tenant-admin-1', actorType: 'TenantAdmin', permissionVersion: 1 },
      CommunicationPermissions.MeetingHost,
      repo,
    );
    expect(result.allowed).toBe(false);
  });

  it('TenantAdmin with the permission in the projection is authorized', async () => {
    const repo = createFakeProjectionRepository([
      fakeSnapshot({ userId: 'u-tenant-admin-2', permissions: [CommunicationPermissions.MeetingHost] }),
    ]);
    const result = await checkPermission(
      { userId: 'u-tenant-admin-2', actorType: 'TenantAdmin', permissionVersion: 1 },
      CommunicationPermissions.MeetingHost,
      repo,
    );
    expect(result.allowed).toBe(true);
  });

  it('regular actor needs the exact permission', async () => {
    const repoWithout = createFakeProjectionRepository([fakeSnapshot({ userId: 'u-employee-1', permissions: [] })]);
    const denied = await checkPermission(
      { userId: 'u-employee-1', actorType: 'TenantEmployee', permissionVersion: 1 },
      CommunicationPermissions.ChatStart,
      repoWithout,
    );
    expect(denied.allowed).toBe(false);

    const repoWith = createFakeProjectionRepository([
      fakeSnapshot({ userId: 'u-employee-2', permissions: ['communication.chat.start'] }),
    ]);
    const granted = await checkPermission(
      { userId: 'u-employee-2', actorType: 'TenantEmployee', permissionVersion: 1 },
      CommunicationPermissions.ChatStart,
      repoWith,
    );
    expect(granted.allowed).toBe(true);
  });

  it('a JWT permissionVersion older than the projection is rejected as stale (Auth.TokenStale)', async () => {
    const repo = createFakeProjectionRepository([
      fakeSnapshot({ userId: 'u-employee-3', permissions: [CommunicationPermissions.ChatStart], permissionVersion: 2 }),
    ]);
    const result = await checkPermission(
      { userId: 'u-employee-3', actorType: 'TenantEmployee', permissionVersion: 1 },
      CommunicationPermissions.ChatStart,
      repo,
    );
    expect(result.allowed).toBe(false);
    if (!result.allowed) expect(result.code).toBe('Auth.TokenStale');
  });

  it('a user with no projection row yet fails closed (never synced / consumer lag)', async () => {
    const repo = createFakeProjectionRepository([]);
    const result = await checkPermission(
      { userId: 'u-never-synced', actorType: 'TenantEmployee', permissionVersion: 1 },
      CommunicationPermissions.ChatStart,
      repo,
    );
    expect(result.allowed).toBe(false);
    if (!result.allowed) expect(result.code).toBe('Auth.Forbidden');
  });
});

describe('correlationId', () => {
  it('normalizes a valid id', () => {
    expect(normalizeCorrelationId('taxvision-check-001')).toBe('taxvision-check-001');
  });

  it('replaces invalid id with generated one', () => {
    const bad = 'bad space \n';
    const out = normalizeCorrelationId(bad);
    expect(out).not.toBe(bad);
    expect(out.length).toBeGreaterThan(0);
  });
});
