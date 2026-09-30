import { describe, expect, it } from 'vitest';
import {
  checkPermissionForActor,
  CommunicationPermissions,
} from '../../src/domain/shared/permissions.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';

/**
 * `portal.calls.use` es la palanca por cliente del cajon de accesos: solo se le exige al actor
 * CustomerPortal. Es el equivalente en Node de `[HasPermissionForActor]` del lado .NET, y existe por
 * el mismo motivo — apilarle el permiso a todos dejaria al staff sin poder llamar.
 */

const CLIENT_WITH = 'u-portal-con-permiso';
const CLIENT_WITHOUT = 'u-portal-sin-permiso';
const CLIENT_STALE = 'u-portal-token-viejo';
const STAFF = 'u-staff';

const repo = createFakeProjectionRepository([
  fakeSnapshot({
    userId: CLIENT_WITH,
    actorType: 'CustomerPortal',
    permissions: [CommunicationPermissions.CallStart, CommunicationPermissions.PortalCallsUse],
  }),
  fakeSnapshot({
    userId: CLIENT_WITHOUT,
    actorType: 'CustomerPortal',
    permissions: [CommunicationPermissions.CallStart],
  }),
  fakeSnapshot({
    userId: CLIENT_STALE,
    actorType: 'CustomerPortal',
    permissions: [CommunicationPermissions.CallStart, CommunicationPermissions.PortalCallsUse],
    permissionVersion: 7,
  }),
  fakeSnapshot({
    userId: STAFF,
    actorType: 'TenantEmployee',
    permissions: [CommunicationPermissions.CallStart],
  }),
]);

function subject(userId: string, actorType: string, permissionVersion = 1) {
  return { userId, tenantId: 'tenant-fake', actorType, permissionVersion };
}

describe('checkPermissionForActor — portal.calls.use', () => {
  it('deja pasar al cliente que lo tiene', async () => {
    const result = await checkPermissionForActor(
      subject(CLIENT_WITH, 'CustomerPortal'),
      'CustomerPortal',
      CommunicationPermissions.PortalCallsUse,
      repo,
    );

    expect(result.allowed).toBe(true);
  });

  it('rechaza al cliente al que se lo quitaron', async () => {
    const result = await checkPermissionForActor(
      subject(CLIENT_WITHOUT, 'CustomerPortal'),
      'CustomerPortal',
      CommunicationPermissions.PortalCallsUse,
      repo,
    );

    expect(result.allowed).toBe(false);
    if (!result.allowed) expect(result.code).toBe('Auth.Forbidden');
  });

  it('no se lo exige al staff, aunque no lo tenga', async () => {
    const result = await checkPermissionForActor(
      subject(STAFF, 'TenantEmployee'),
      'CustomerPortal',
      CommunicationPermissions.PortalCallsUse,
      repo,
    );

    expect(result.allowed).toBe(true);
  });

  it('no se lo exige a un invitado de meeting', async () => {
    const result = await checkPermissionForActor(
      subject('u-guest', 'Guest'),
      'CustomerPortal',
      CommunicationPermissions.PortalCallsUse,
      repo,
    );

    expect(result.allowed).toBe(true);
  });

  it('con un token anterior al cambio de permisos pide refrescar, no deniega', async () => {
    const result = await checkPermissionForActor(
      subject(CLIENT_STALE, 'CustomerPortal', 3),
      'CustomerPortal',
      CommunicationPermissions.PortalCallsUse,
      repo,
    );

    expect(result.allowed).toBe(false);
    if (!result.allowed) expect(result.code).toBe('Auth.TokenStale');
  });

  it('un cliente sin proyeccion queda fuera (fail-closed)', async () => {
    const result = await checkPermissionForActor(
      subject('u-desconocido', 'CustomerPortal'),
      'CustomerPortal',
      CommunicationPermissions.PortalCallsUse,
      repo,
    );

    expect(result.allowed).toBe(false);
  });
});
